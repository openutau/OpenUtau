#include "plugin.hpp"
#include "DistrhoDetails.hpp"
#include "DistrhoPlugin.hpp"
#include "DistrhoPluginInfo.h"
#include "asio.hpp"
#include "choc/containers/choc_Value.h"
#include "choc/memory/choc_Base64.h"
#include "choc/text/choc_JSON.h"
#include "common.hpp"
#include "dpf/distrho/extra/String.hpp"
#include "gzip/compress.hpp"
#include "uuid/v4/uuid.h"
#include <array>
#include <cfloat>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <set>
#include <shared_mutex>
#include <string>
#include <thread>
#include <utility>
#include <vector>

namespace Network {
std::shared_ptr<std::jthread> ioThread;
std::shared_ptr<asio::io_context> ioContext;
std::shared_ptr<asio::io_context> getIoContext() {
  if (ioThread == nullptr) {
    if (ioContext == nullptr) {
      ioContext = std::make_shared<asio::io_context>();
    }
    ioThread = std::make_shared<std::jthread>([](std::stop_token st) {
      while (!st.stop_requested()) {
        ioContext->run();
      }
    });
    std::atexit([]() {
      ioContext->stop();
      ioThread->request_stop();
      ioThread->join();
    });
  }

  return ioContext;
}
} // namespace Network

// note: OpenUtau returns 44100Hz, 2ch, 32bit float audio

choc::value::Value Part::serialize() const {
  auto obj = choc::value::createObject("", "trackNo", trackNo, "startMs",
                                       startMs, "endMs", endMs);
  if (hash.has_value()) {
    // choc cannot set uint32_t, so we need to cast it to int64_t
    obj.setMember("audioHash", (int64_t)hash.value());
  }

  return obj;
}
Part Part::deserialize(const choc::value::ValueView &value) {
  Part part;
  part.trackNo = value["trackNo"].get<int>();
  part.startMs = value["startMs"].get<double>();
  part.endMs = value["endMs"].get<double>();
  auto audioHash = value["audioHash"];
  if (audioHash.isVoid()) {
    part.hash = std::nullopt;
  } else {
    part.hash = (uint32_t)audioHash.get<int64_t>();
  }
  return part;
}

// -----------------------------------------------------------------------------------------------------------
OpenUtauPlugin::OpenUtauPlugin()
    : Plugin(0, 0, 6)

{

  if (this->isDummyInstance()) {
    return;
  }

  std::string uuid = uuid::v4::UUID::New().String();
  setState("uuid", uuid.c_str());

  setState("name", uuid.c_str());

  this->resampleThread =
      std::jthread([this](std::stop_token st) { resampleWorkerLoop(st); });
  initializeNetwork();
}
OpenUtauPlugin::~OpenUtauPlugin() {
  if (this->isDummyInstance()) {
    return;
  }

  if (std::filesystem::exists(this->socketPath)) {
    std::filesystem::remove(this->socketPath);
  }

  this->shuttingDown = true;
  if (this->acceptor != nullptr) {
    asio::error_code error;
    this->acceptor->close(error);
  }
  closeActiveSocket();
  this->connectionThread.request_stop();
  this->resampleThread.request_stop();
  this->resampleRequestCondition.notify_all();
}

const char *OpenUtauPlugin::getLabel() const {
#ifdef DEBUG
  return "OpenUtau_Debug";
#else
  return "OpenUtau";
#endif
}

const char *OpenUtauPlugin::getDescription() const {
  return "Bridge between OpenUtau and your DAW";
}

const char *OpenUtauPlugin::getMaker() const { return "stakira"; }

const char *OpenUtauPlugin::getHomePage() const {
  return "https://github.com/stakira/OpenUtau/";
}

void OpenUtauPlugin::initState(uint32_t index, State &state) {
  switch (index) {
  case 0:
    state.key = "name";
    state.label = "Plugin Name";
    state.defaultValue = "";
    break;
  case 1:
    state.key = "ustx";
    state.label = "USTx";
    state.hints = kStateIsBase64Blob;
    break;
  case 2:
    state.key = "audios";
    state.label = "Audios";
    state.hints = kStateIsBase64Blob;
    break;
  case 3:
    state.key = "parts";
    state.label = "Parts";
    break;
  case 4:
    state.key = "tracks";
    state.label = "Tracks";
    state.hints = kStateIsBase64Blob;
    break;
  case 5:
    state.key = "mapping";
    state.label = "Output Mapping";
    break;
  }
}

String OpenUtauPlugin::getState(const char *rawKey) const {
  // DPF cannot handle binary data, so we need to encode it to base64
  std::string key(rawKey);

  if (key == "name") {
    auto _lock = std::lock_guard(this->stateMutex);
    return String(name.c_str());
  } else if (key == "uuid") {
    auto _lock = std::lock_guard(this->stateMutex);
    return String(uuid.c_str());
  } else if (key == "ustx") {
    auto _lock = std::lock_guard(this->stateMutex);
    std::string encoded = choc::base64::encodeToString(ustx);
    return String(encoded.c_str());
  } else if (key == "audios") {
    auto _lock = std::shared_lock(this->audioBuffersMutex);
    choc::value::Value value = choc::value::createObject("");
    for (const auto &[audioHash, audio] : audioBuffers) {
      std::string compressed =
          gzip::compress((char *)audio.data(), audio.size() * sizeof(float));
      std::string encoded = choc::base64::encodeToString(compressed);
      value.setMember(std::to_string(audioHash), encoded);
    }

    return String(choc::json::toString(value).c_str());
  } else if (key == "parts") {
    auto _lock = std::shared_lock(this->partsMutex);
    choc::value::Value value = choc::value::createEmptyArray();
    for (const auto &[trackNo, parts] : parts) {
      for (const auto &part : parts) {
        value.addArrayElement(part.serialize());
      }
    }
    return String(choc::json::toString(value).c_str());
  } else if (key == "tracks") {
    auto _lock = std::shared_lock(this->tracksMutex);
    return String(Structures::serializeTracks(tracks).c_str());
  } else if (key == "mapping") {
    auto _lock = std::shared_lock(this->tracksMutex);
    return String(Structures::serializeOutputMap(outputMap).c_str());
  }
  return String();
}

void OpenUtauPlugin::setState(const char *rawKey, const char *value) {
  std::string key(rawKey);
  if (key == "name") {
    setPluginName(value);
  } else if (key == "uuid") {
    auto _lock = std::lock_guard(this->stateMutex);
    this->uuid = value;
  } else if (key == "ustx") {
    auto _lock = std::lock_guard(this->stateMutex);
    this->ustx = Utils::unBase64ToString(value);
  } else if (key == "audios") {
    choc::value::Value audioValue = choc::json::parse(value);
    std::map<AudioHash, std::vector<float>> audioBuffers;
    choc::value::ValueView(audioValue)
        .visitObjectMembers(
            [&](std::string_view key, const choc::value::ValueView &value) {
              auto hash = std::stoul(std::string(key));
              std::string encoded = value.get<std::string>();
              auto decoded = Utils::unBase64ToVector(encoded);
              auto decompressed =
                  Utils::gunzip((char *)decoded.data(), decoded.size());
              std::vector<float> audio((float *)decompressed.data(),
                                       (float *)decompressed.data() +
                                           decompressed.size() / sizeof(float));
              audioBuffers[hash] = audio;
            });

    {
      auto _lock = std::lock_guard(this->audioBuffersMutex);
      this->audioBuffers = audioBuffers;
    }
    this->requestResampleMixes(this->currentSampleRate.load());
  } else if (key == "parts") {
    choc::value::Value partsValue = choc::json::parse(value);
    std::map<int, std::vector<Part>> parts;
    for (const auto &partValue : partsValue) {
      Part part = Part::deserialize(partValue);
      if (parts.find(part.trackNo) == parts.end()) {
        parts[part.trackNo] = std::vector<Part>();
      }
      parts[part.trackNo].push_back(part);
    }
    {
      auto _lock = std::lock_guard(this->partsMutex);
      this->parts = parts;
    }
    this->requestResampleMixes(this->currentSampleRate.load());
  } else if (key == "tracks") {
    auto _lock = std::lock_guard(this->tracksMutex);
    this->tracks = Structures::deserializeTracks(value);
  } else if (key == "mapping") {
    setOutputMap(Structures::deserializeOutputMap(value));
  }
}

const char *OpenUtauPlugin::getLicense() const { return "MIT"; }

uint32_t OpenUtauPlugin::getVersion() const {
  return d_version(Constants::majorVersion, Constants::minorVersion,
                   Constants::patchVersion);
}

void OpenUtauPlugin::initAudioPort(bool input, uint32_t index,
                                   AudioPort &port) {
  port.groupId = index / 2;
  auto name = std::format("Channel {}", index / 2 + 1);
  auto symbol =
      std::format("channel-{}-{}", index / 2, index % 2 == 0 ? "l" : "r");
  port.name = String(name.c_str());
  port.symbol = String(symbol.c_str());
}
void OpenUtauPlugin::initPortGroup(uint32_t groupId, PortGroup &group) {
  auto name = std::format("Group {}", groupId + 1);
  auto symbol = std::format("group-{}", groupId);
  group.symbol = String(symbol.c_str());
  group.name = String(name.c_str());
}

void OpenUtauPlugin::run(const float **inputs, float **outputs, uint32_t frames,
                         const MidiEvent *midiEvents, uint32_t midiEventCount) {

  auto timePosition = this->getTimePosition();
  const bool wasPlaying = this->wasPlaying.exchange(timePosition.playing);
  if (timePosition.playing && !wasPlaying) {
    this->playbackStartedPending.store(true);
  }

  for (uint32_t i = 0; i < DISTRHO_PLUGIN_NUM_OUTPUTS; ++i) {
    for (uint32_t j = 0; j < frames; ++j) {
      outputs[i][j] = 0;
    }
  }

  auto sampleRate = getSampleRate();
  auto mixLock = std::shared_lock(this->mixMutex, std::defer_lock);
  auto tracksLock = std::shared_lock(this->tracksMutex, std::defer_lock);
  if (timePosition.playing && mixLock.try_lock() && tracksLock.try_lock()) {
    if (this->currentSampleRate.load() == sampleRate) {
      for (uint32_t j = 0; j < mixes.size(); ++j) {
        if (j >= this->outputMap.size()) {
          break;
        }
        if (j >= this->tracks.size()) {
          break;
        }
        const auto &mapping = outputMap[j];
        const auto &left = mixes[j].first;
        const auto &right = mixes[j].second;

        const auto &track = tracks[j];

        for (uint32_t i = 0; i < frames; ++i) {
          auto frame = (i + timePosition.frame);
          if (frame >= left.size()) {
            break;
          }
          if (frame >= right.size()) {
            break;
          }
          auto fadedLeft = left[frame] * Utils::dbToMultiplier(track.volume);
          auto fadedRight = right[frame] * Utils::dbToMultiplier(track.volume);
          if (track.pan < 0) {
            fadedRight *= 1 + (track.pan / 100.0);
          } else if (track.pan > 0) {
            fadedLeft *= 1 - (track.pan / 100.0);
          }
          for (uint32_t k = 0; k < DISTRHO_PLUGIN_NUM_OUTPUTS; ++k) {
            if (mapping.first[k] && frame < left.size()) {
              if (outputs[k][i] > FLT_MAX - fadedLeft) {
                outputs[k][i] = FLT_MAX;
              } else if (outputs[k][i] < -FLT_MAX + fadedLeft) {
                outputs[k][i] = -FLT_MAX;
              } else {
                outputs[k][i] += fadedLeft;
              }
            }
            if (mapping.second[k] && frame < right.size()) {
              if (outputs[k][i] > FLT_MAX - fadedRight) {
                outputs[k][i] = FLT_MAX;
              } else if (outputs[k][i] < -FLT_MAX + fadedRight) {
                outputs[k][i] = -FLT_MAX;
              } else {
                outputs[k][i] += fadedRight;
              }
            }
          }
        }
      }
    } else {
      requestResampleMixes(sampleRate, false);
    }
  }
};

void OpenUtauPlugin::sampleRateChanged(double newSampleRate) {
  requestResampleMixes(newSampleRate);
}

void OpenUtauPlugin::onAccept(OpenUtauPlugin *self,
                              const asio::error_code &error,
                              asio::ip::tcp::socket socket) {
  if (self->shuttingDown) {
    return;
  }
  self->willAccept();
  if (error) {
    if (error != asio::error::operation_aborted) {
      self->setConnectionState(ConnectionState::Error, error.message());
    }
    return;
  }

  auto socketPtr =
      std::make_shared<asio::ip::tcp::socket>(std::move(socket));
  {
    auto lock = std::lock_guard(self->connectionMutex);
    if (self->activeSocket != nullptr) {
      asio::error_code closeError;
      socketPtr->close(closeError);
      return;
    }
    self->activeSocket = socketPtr;
  }

  if (self->connectionThread.joinable()) {
    self->connectionThread.join();
  }
  self->connectionThread = std::jthread(
      [self, socketPtr](std::stop_token st) {
        self->connectionLoop(st, socketPtr);
      });
}

void OpenUtauPlugin::willAccept() {
  if (this->shuttingDown || this->acceptor == nullptr ||
      !this->acceptor->is_open()) {
    return;
  }
  acceptor->async_accept(std::bind(&OpenUtauPlugin::onAccept, this,
                                   std::placeholders::_1,
                                   std::placeholders::_2));
}

void OpenUtauPlugin::connectionLoop(
    std::stop_token stopToken,
  std::shared_ptr<asio::ip::tcp::socket> socket) {
  setConnectionState(ConnectionState::Connected);
  std::string disconnectError;
  asio::error_code nonBlockingError;
  socket->non_blocking(true, nonBlockingError);
  if (nonBlockingError) {
    disconnectError = nonBlockingError.message();
  } else {
    std::string messageBuffer;
    std::array<char, 16 * 1024> buffer;
    std::jthread heartbeatThread(
        [this, socket](std::stop_token heartbeatStopToken) {
          int heartbeatTicks = 0;
          while (!heartbeatStopToken.stop_requested() &&
                 !this->shuttingDown) {
            std::this_thread::sleep_for(std::chrono::milliseconds(100));
            if (heartbeatStopToken.stop_requested() || this->shuttingDown) {
              return;
            }
            try {
              if (this->playbackStartedPending.exchange(false)) {
                sendMessage(socket,
                            formatMessage("notification:playbackStarted",
                                          choc::value::createObject("")));
              }
              if (++heartbeatTicks >= 50) {
                heartbeatTicks = 0;
                sendMessage(socket,
                            formatMessage("notification:ping",
                                          choc::value::createObject("")));
              }
            } catch (const std::exception &) {
              asio::error_code error;
              socket->close(error);
              return;
            }
          }
        });

    while (!stopToken.stop_requested() && !this->shuttingDown) {
      asio::error_code error;
      const auto len = socket->read_some(asio::buffer(buffer), error);
      if (!error) {
        if (len == 0) {
          disconnectError = "Connection closed by OpenUtau";
          break;
        }
        messageBuffer.append(buffer.data(), len);
        size_t pos;
        while ((pos = messageBuffer.find('\n')) != std::string::npos) {
          auto message = messageBuffer.substr(0, pos);
          messageBuffer.erase(0, pos + 1);
          if (message == "close") {
            disconnectError.clear();
            goto connection_finished;
          }
          try {
            processMessage(message, socket);
          } catch (const std::exception &e) {
            disconnectError = e.what();
            goto connection_finished;
          }
        }
      } else if (error != asio::error::would_block &&
                 error != asio::error::try_again) {
        if (error != asio::error::operation_aborted) {
          disconnectError = error.message();
        }
        break;
      }

      std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }
  }

connection_finished:
  {
    auto lock = std::lock_guard(this->connectionMutex);
    if (this->activeSocket == socket) {
      this->activeSocket.reset();
    }
  }
  asio::error_code closeError;
  socket->close(closeError);
  if (this->shuttingDown || stopToken.stop_requested() ||
      disconnectError.empty()) {
    setConnectionState(ConnectionState::Disconnected);
  } else {
    setConnectionState(ConnectionState::Error, disconnectError);
  }
}

void OpenUtauPlugin::processMessage(
    const std::string &message,
    const std::shared_ptr<asio::ip::tcp::socket> &socket) {
  const auto separator = message.find(' ');
  if (separator == std::string::npos) {
    throw std::runtime_error("Malformed message: missing payload");
  }
  const auto header = message.substr(0, separator);
  const auto payload = choc::json::parse(message.substr(separator + 1));
  const auto firstColon = header.find(':');
  if (firstColon == std::string::npos) {
    throw std::runtime_error("Malformed message header");
  }

  const auto messageType = header.substr(0, firstColon);
  if (messageType == "request") {
    const auto secondColon = header.find(':', firstColon + 1);
    if (secondColon == std::string::npos) {
      throw std::runtime_error("Malformed request header");
    }
    const auto messageId =
        header.substr(firstColon + 1, secondColon - firstColon - 1);
    const auto requestType = header.substr(secondColon + 1);
    auto responseObject = choc::value::createObject("");
    try {
      responseObject.setMember("success", true);
      responseObject.setMember("data", onRequest(requestType, payload));
    } catch (const std::exception &e) {
      responseObject.setMember("success", false);
      responseObject.setMember("error", e.what());
    }
    sendMessage(socket, formatMessage(std::format("response:{}", messageId),
                                      responseObject));
  } else if (messageType == "notification") {
    onNotification(header.substr(firstColon + 1), payload);
  } else {
    throw std::runtime_error("Unknown message type");
  }
}

void OpenUtauPlugin::sendMessage(
    const std::shared_ptr<asio::ip::tcp::socket> &socket,
    const std::string &message) {
  auto lock = std::lock_guard(this->socketWriteMutex);
  asio::error_code error;
  asio::write(*socket, asio::buffer(message), error);
  if (error) {
    throw asio::system_error(error);
  }
}

void OpenUtauPlugin::closeActiveSocket() {
  std::shared_ptr<asio::ip::tcp::socket> socket;
  {
    auto lock = std::lock_guard(this->connectionMutex);
    socket = std::exchange(this->activeSocket, nullptr);
  }
  if (socket != nullptr) {
    asio::error_code error;
    socket->shutdown(asio::ip::tcp::socket::shutdown_both, error);
    socket->close(error);
  }
}

void OpenUtauPlugin::setConnectionState(ConnectionState state,
                                         const std::string &error) {
  auto lock = std::lock_guard(this->stateMutex);
  this->connectionState = state;
  this->lastError = error;
}

void OpenUtauPlugin::initializeNetwork() {
  this->acceptor = std::make_unique<asio::ip::tcp::acceptor>(
      Network::getIoContext()->get_executor(),
      asio::ip::tcp::endpoint(asio::ip::make_address("127.0.0.1"), 0));

  {
    auto lock = std::lock_guard(this->stateMutex);
    this->port = this->acceptor->local_endpoint().port();
  }
  updatePluginServerFile();
  willAccept();
}

void OpenUtauPlugin::updatePluginServerFile() {
  int port;
  std::string name;
  std::string uuid;
  {
    auto lock = std::lock_guard(this->stateMutex);
    port = this->port;
    name = this->name;
    uuid = this->uuid;
  }
  std::filesystem::path tempPath = std::filesystem::temp_directory_path();
  std::filesystem::path socketPath = tempPath / "OpenUtau" / "PluginServers" /
                                     std::format("{}.json", uuid);
  std::string socketContent = choc::json::toString(
      choc::value::createObject("", "port", port, "name", name));

  std::filesystem::create_directories(socketPath.parent_path());
  std::ofstream socketFile(socketPath);
  socketFile << socketContent;
  socketFile.close();

  this->socketPath = socketPath;
}

choc::value::Value OpenUtauPlugin::onRequest(const std::string kind,
                                             const choc::value::Value payload) {
  if (kind == "init") {
    auto _lock = std::lock_guard(this->stateMutex);
    choc::value::Value response =
        choc::value::createObject("", "ustx", this->ustx);
    return response;
  } else if (kind == "updatePartLayout") {
    std::map<int, std::vector<Part>> parts;
    std::vector<Part> flatParts;
    std::set<AudioHash> hashes;
    for (const auto &part : payload["parts"]) {
      flatParts.push_back(Part::deserialize(part));
    }
    for (const auto &part : flatParts) {
      if (parts.find(part.trackNo) == parts.end()) {
        parts[part.trackNo] = std::vector<Part>();
      }
      parts[part.trackNo].push_back(part);
      if (part.hash.has_value()) {
        hashes.insert(part.hash.value());
      }
    }
    {
      auto _lock = std::lock_guard(this->partsMutex);
      this->parts = parts;
    }
    std::set<AudioHash> toAdd;
    {
      auto _lock = std::unique_lock(this->audioBuffersMutex);
      for (auto it = this->audioBuffers.begin();
           it != this->audioBuffers.end();) {
        if (!hashes.contains(it->first)) {
          it = this->audioBuffers.erase(it);
        } else {
          ++it;
        }
      }
      for (const auto &hash : hashes) {
        if (!this->audioBuffers.contains(hash)) {
          toAdd.insert(hash);
        }
      }
    }

    choc::value::Value response = choc::value::createObject("");
    auto missingAudios = choc::value::createEmptyArray();
    for (const auto &hash : toAdd) {
      missingAudios.addArrayElement(std::to_string(hash));
    }
    response.setMember("missingAudios", missingAudios);

    this->requestResampleMixes(this->currentSampleRate.load());

    if (toAdd.size() == 0) {
      auto _lock = std::lock_guard(this->stateMutex);
      this->lastSync = std::chrono::system_clock::now();
    }

    return response;
  }

  throw std::runtime_error("Unknown request type");
}
void OpenUtauPlugin::onNotification(const std::string kind,
                                    const choc::value::Value payload) {
  if (kind == "updateUstx") {
    auto ustx = payload["ustx"].get<std::string>();
    auto ustxBase64 = choc::base64::encodeToString(ustx);
    setState("ustx", ustxBase64.c_str());
  } else if (kind == "updateTracks") {
    {
      auto _lock = std::unique_lock(this->tracksMutex);
      auto tracks = std::vector<Structures::Track>();
      for (const auto &track : payload["tracks"]) {
        tracks.push_back(Structures::Track::deserialize(track));
      }

      this->tracks = tracks;
    }
    syncMapping();
  } else if (kind == "updateAudio") {
    {
      auto _lock = std::unique_lock(this->audioBuffersMutex);

      auto audioBuffers = this->audioBuffers;
      payload["audios"].visitObjectMembers(
          [&](std::string_view key, const choc::value::ValueView &value) {
            auto hash = std::stoul(std::string(key));
            std::string encoded = value.get<std::string>();
            auto decoded = Utils::unBase64ToVector(encoded);
            auto decompressed =
                Utils::gunzip((char *)decoded.data(), decoded.size());
            std::vector<float> audio((float *)decompressed.data(),
                                     (float *)decompressed.data() +
                                         decompressed.size() / sizeof(float));
            audioBuffers[hash] = audio;
          });

      this->audioBuffers = audioBuffers;
    }

    {
      auto _lock = std::lock_guard(this->stateMutex);
      this->lastSync = std::chrono::system_clock::now();
    }
    this->requestResampleMixes(this->currentSampleRate.load());
  }
}

void OpenUtauPlugin::syncMapping() {
  Structures::OutputMap newOutputMap;
  {
    auto _lock = std::unique_lock(this->tracksMutex);
    newOutputMap = this->outputMap;
    if (tracks.size() < newOutputMap.size()) {
      newOutputMap.resize(tracks.size());
    } else if (tracks.size() > newOutputMap.size()) {
      for (size_t i = newOutputMap.size(); i < tracks.size(); ++i) {
        auto leftChannel = std::bitset<DISTRHO_PLUGIN_NUM_OUTPUTS>();
        auto rightChannel = std::bitset<DISTRHO_PLUGIN_NUM_OUTPUTS>();
        leftChannel[0] = true;
        rightChannel[1] = true;

        newOutputMap.push_back({leftChannel, rightChannel});
      }
    }
    this->outputMap = newOutputMap;
  }
  setState("mapping", Structures::serializeOutputMap(newOutputMap).c_str());
}

void OpenUtauPlugin::requestResampleMixes(double newSampleRate, bool force) {
  {
    auto lock = std::lock_guard(this->resampleRequestMutex);
    if (!force && this->requestedSampleRate == newSampleRate &&
        this->requestedResampleGeneration > 0) {
      return;
    }
    this->requestedSampleRate = newSampleRate;
    ++this->requestedResampleGeneration;
  }
  this->resampleRequestCondition.notify_one();
}

void OpenUtauPlugin::resampleWorkerLoop(std::stop_token stopToken) {
  uint64_t completedGeneration = 0;
  while (!stopToken.stop_requested()) {
    double sampleRate;
    uint64_t generation;
    {
      auto lock = std::unique_lock(this->resampleRequestMutex);
      this->resampleRequestCondition.wait(
          lock, stopToken, [&] {
            return this->requestedResampleGeneration > completedGeneration;
          });
      if (stopToken.stop_requested()) {
        return;
      }
      sampleRate = this->requestedSampleRate;
      generation = this->requestedResampleGeneration;
    }
    resampleMixes(sampleRate, generation);
    completedGeneration = generation;
  }
}

void OpenUtauPlugin::resampleMixes(double newSampleRate,
                                   uint64_t generation) {
  this->mixMutexLocked.store(true);
  std::map<AudioHash, std::vector<float>> audioBuffers;
  std::map<int, std::vector<Part>> parts;
  {
    auto lock = std::shared_lock(this->audioBuffersMutex);
    audioBuffers = this->audioBuffers;
  }
  {
    auto lock = std::shared_lock(this->partsMutex);
    parts = this->parts;
  }

  std::vector<std::pair<std::vector<float>, std::vector<float>>> mixes;
  for (const auto &[trackNo, trackParts] : parts) {
    if (trackNo < 0) {
      continue;
    }
    std::vector<float> resampledLeft;
    std::vector<float> resampledRight;
    if (mixes.size() <= static_cast<size_t>(trackNo)) {
      mixes.resize(static_cast<size_t>(trackNo) + 1);
    }
    if (trackParts.empty()) {
      continue;
    }
    auto maxEndMs = std::max_element(
        trackParts.begin(), trackParts.end(),
        [](const Part &a, const Part &b) { return a.endMs < b.endMs; });
    resampledLeft.resize((size_t)(maxEndMs->endMs / 1000.0 * newSampleRate) +
                         1);
    resampledRight.resize((size_t)(maxEndMs->endMs / 1000.0 * newSampleRate) +
                          1);
    for (const auto &part : trackParts) {
      if (!part.hash.has_value()) {
        continue;
      }
      auto startFrame = (size_t)(part.startMs / 1000.0 * newSampleRate);
      auto endFrame = (size_t)(part.endMs / 1000.0 * newSampleRate);
      auto rate = 44100.0 / newSampleRate;
      const auto audio = audioBuffers.find(part.hash.value());
      if (audio == audioBuffers.end()) {
        continue;
      }
      const auto &buffer = audio->second;

      for (size_t i = startFrame; i < endFrame; ++i) {
        auto frame = (size_t)((i - startFrame) * rate);
        auto leftFrameLeft = frame * 2;
        auto leftFrameRight = frame * 2 + 2;
        auto rightFrameLeft = frame * 2 + 1;
        auto rightFrameRight = frame * 2 + 3;
        auto lerp = ((i - startFrame) * rate) - frame;
        if (rightFrameRight >= buffer.size()) {
          break;
        }
        auto left =
            (1 - lerp) * buffer[leftFrameLeft] + lerp * buffer[leftFrameRight];
        auto right = (1 - lerp) * buffer[rightFrameLeft] +
                     lerp * buffer[rightFrameRight];
        if (resampledLeft[i] > FLT_MAX - left) {
          resampledLeft[i] = FLT_MAX;
        } else if (resampledLeft[i] < -FLT_MAX + left) {
          resampledLeft[i] = -FLT_MAX;
        } else {
          resampledLeft[i] += left;
        }
        if (resampledRight[i] > FLT_MAX - right) {
          resampledRight[i] = FLT_MAX;
        } else if (resampledRight[i] < -FLT_MAX + right) {
          resampledRight[i] = -FLT_MAX;
        } else {
          resampledRight[i] += right;
        }
      }
    }

    mixes[trackNo] = {std::move(resampledLeft), std::move(resampledRight)};
  }

  {
    auto requestLock = std::lock_guard(this->resampleRequestMutex);
    if (generation != this->requestedResampleGeneration) {
      this->mixMutexLocked.store(false);
      return;
    }
  }
  {
    auto mixLock = std::unique_lock(this->mixMutex);
    this->mixes = std::move(mixes);
    this->currentSampleRate.store(newSampleRate);
  }
  this->mixMutexLocked.store(false);
}

std::string
OpenUtauPlugin::formatMessage(const std::string &kind,
                              const choc::value::ValueView &payload) {
  std::string json = choc::json::toString(payload);
  return std::format("{} {}\n", kind, json);
}

OpenUtauPlugin::UiSnapshot OpenUtauPlugin::getUiSnapshot() const {
  UiSnapshot snapshot;
  {
    auto lock = std::lock_guard(this->stateMutex);
    snapshot.port = this->port;
    snapshot.connectionState = this->connectionState;
    snapshot.name = this->name;
    snapshot.lastError = this->lastError;
    snapshot.lastSync = this->lastSync;
  }
  {
    auto lock = std::shared_lock(this->tracksMutex);
    snapshot.tracks = this->tracks;
    snapshot.outputMap = this->outputMap;
  }
  snapshot.processing = this->mixMutexLocked.load();
  return snapshot;
}

void OpenUtauPlugin::setPluginName(const std::string &newName) {
  {
    auto lock = std::lock_guard(this->stateMutex);
    this->name = newName;
  }
  updatePluginServerFile();
}

void OpenUtauPlugin::setOutputMap(
    const Structures::OutputMap &newOutputMap) {
  auto lock = std::unique_lock(this->tracksMutex);
  this->outputMap = newOutputMap;
}

// ------------------------------------------------------------------------------------------------------------

START_NAMESPACE_DISTRHO
Plugin *createPlugin() { return new OpenUtauPlugin(); }
END_NAMESPACE_DISTRHO

// -----------------------------------------------------------------------------------------------------------
