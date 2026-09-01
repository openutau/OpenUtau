#pragma once
#include "DistrhoPlugin.hpp"
#include "alternate_shared_mutex.hpp"
#include "asio.hpp"
#include "choc/containers/choc_Value.h"
#include "common.hpp"
#include "extra/String.hpp"
#include "yamc_rwlock_sched.hpp"
#include <atomic>
#include <condition_variable>
#include <filesystem>
#include <map>
#include <memory>
#include <mutex>
#include <optional>
#include <string>
#include <thread>
#include <vector>

// note: OpenUtau returns 44100Hz, 2ch, 32bit float audio

using AudioHash = uint32_t;
class Part {
public:
  int trackNo;
  double startMs;
  double endMs;

  std::optional<AudioHash> hash;

  static Part deserialize(const choc::value::ValueView &value);
  choc::value::Value serialize() const;
};

// -----------------------------------------------------------------------------------------------------------

class OpenUtauPlugin : public Plugin {
public:
  enum class ConnectionState {
    Disconnected,
    Connected,
    Error,
  };

  struct UiSnapshot {
    int port;
    ConnectionState connectionState;
    std::string name;
    std::string lastError;
    std::optional<std::chrono::time_point<std::chrono::system_clock>> lastSync;
    std::vector<Structures::Track> tracks;
    Structures::OutputMap outputMap;
    bool processing;
  };

  OpenUtauPlugin();

  ~OpenUtauPlugin() override;

  UiSnapshot getUiSnapshot() const;
  void setPluginName(const std::string &newName);
  void setOutputMap(const Structures::OutputMap &newOutputMap);

protected:
  // --------------------------------------------------------------------------------------------------------
  const char *getLabel() const override;

  const char *getDescription() const override;

  const char *getMaker() const override;

  const char *getHomePage() const override;

  void initState(uint32_t index, State &state) override;

  String getState(const char *key) const override;

  void setState(const char *key, const char *value) override;

  const char *getLicense() const override;

  uint32_t getVersion() const override;

  // --------------------------------------------------------------------------------------------------------
  void initAudioPort(bool input, uint32_t index, AudioPort &port) override;
  void initPortGroup(uint32_t groupId, PortGroup &group) override;

  // --------------------------------------------------------------------------------------------------------

  void run(const float **inputs, float **outputs, uint32_t frames,
           const MidiEvent *midiEvents, uint32_t midiEventCount) override;

  // --------------------------------------------------------------------------------------------------------

  void sampleRateChanged(double newSampleRate) override;

  // -------------------------------------------------------------------------------------------------------

private:
  static void onAccept(OpenUtauPlugin *self, const asio::error_code &error,
                       asio::ip::tcp::socket socket);

  void willAccept();

  void initializeNetwork();
  void connectionLoop(std::stop_token stopToken,
                      std::shared_ptr<asio::ip::tcp::socket> socket);
  void processMessage(const std::string &message,
                      const std::shared_ptr<asio::ip::tcp::socket> &socket);
  void sendMessage(const std::shared_ptr<asio::ip::tcp::socket> &socket,
                   const std::string &message);
  void closeActiveSocket();
  void setConnectionState(ConnectionState state,
                          const std::string &error = {});

  choc::value::Value onRequest(const std::string kind,
                               const choc::value::Value payload);
  void onNotification(const std::string kind, const choc::value::Value payload);

  static std::string formatMessage(const std::string &kind,
                                   const choc::value::ValueView &payload);

  void syncMapping();
  void updatePluginServerFile();
  void requestResampleMixes(double newSampleRate, bool force = true);
  void resampleWorkerLoop(std::stop_token stopToken);
  void resampleMixes(double newSampleRate, uint64_t generation);

  mutable std::mutex stateMutex;
  int port = 0;
  std::string name;
  std::string ustx;
  std::string uuid;
  ConnectionState connectionState = ConnectionState::Disconnected;
  std::string lastError;
  std::optional<std::chrono::time_point<std::chrono::system_clock>> lastSync;

  mutable yamc::alternate::basic_shared_mutex<yamc::rwlock::WriterPrefer>
      tracksMutex;
  std::vector<Structures::Track> tracks;
  Structures::OutputMap outputMap;

  mutable yamc::alternate::basic_shared_mutex<yamc::rwlock::WriterPrefer>
      audioBuffersMutex;
  std::map<AudioHash, std::vector<float>> audioBuffers;

  mutable yamc::alternate::basic_shared_mutex<yamc::rwlock::WriterPrefer>
      partsMutex;
  std::map<int, std::vector<Part>> parts;

  yamc::alternate::basic_shared_mutex<yamc::rwlock::WriterPrefer> mixMutex;
  std::atomic_bool mixMutexLocked = false;
  std::vector<std::pair<std::vector<float>, std::vector<float>>> mixes;
  std::atomic<double> currentSampleRate = 44100.0;

  std::mutex resampleRequestMutex;
  std::condition_variable_any resampleRequestCondition;
  double requestedSampleRate = 44100.0;
  uint64_t requestedResampleGeneration = 0;
  std::jthread resampleThread;

  std::filesystem::path socketPath;

  std::unique_ptr<asio::ip::tcp::acceptor> acceptor;
  std::mutex connectionMutex;
  std::mutex socketWriteMutex;
  std::shared_ptr<asio::ip::tcp::socket> activeSocket;
  std::jthread connectionThread;
  std::atomic_bool shuttingDown = false;
  std::atomic_bool wasPlaying = false;
  std::atomic_bool playbackStartedPending = false;

  DISTRHO_DECLARE_NON_COPYABLE_WITH_LEAK_DETECTOR(OpenUtauPlugin)
};
// -----------------------------------------------------------------------------------------------------------
