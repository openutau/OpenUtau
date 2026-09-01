#include "common.hpp"
#include "choc/memory/choc_Base64.h"
#include "choc/text/choc_JSON.h"
#include "gzip/decompress.hpp"

std::vector<uint8_t> Utils::gunzip(const char *data, size_t size) {
  std::vector<uint8_t> decompressed;
  gzip::Decompressor decompressor;
  decompressor.decompress(decompressed, data, size);
  return decompressed;
}
std::string Utils::unBase64ToString(const std::string &encoded) {
  std::vector<uint8_t> decoded;
  choc::base64::decodeToContainer(decoded, encoded);
  return std::string(decoded.begin(), decoded.end());
}
std::vector<uint8_t> Utils::unBase64ToVector(const std::string &encoded) {
  std::vector<uint8_t> decoded;
  choc::base64::decodeToContainer(decoded, encoded);
  return decoded;
}

double Utils::dbToMultiplier(double db) {
  return (db <= -24)  ? 0
         : (db < -16) ? std::pow(10, (db * 2 + 16) / 20)
                      : std::pow(10, db / 20);
}

choc::value::Value Structures::Track::serialize() const {
  return choc::value::createObject("", "name", name, "pan", pan, "volume",
                                   volume);
}
Structures::Track
Structures::Track::deserialize(const choc::value::ValueView &value) {
  Track track;
  track.name = value["name"].get<std::string>();
  track.pan = value["pan"].get<float>();
  track.volume = value["volume"].get<float>();
  return track;
}

std::string Structures::serializeTracks(const std::vector<Track> &tracks) {
  choc::value::Value value = choc::value::createEmptyArray();
  for (const auto &track : tracks) {
    value.addArrayElement(track.serialize());
  }
  auto json = choc::json::toString(value);
  return choc::base64::encodeToString(json.data(), json.size());
}
std::vector<Structures::Track>
Structures::deserializeTracks(const std::string &data) {
  auto json = Utils::unBase64ToString(data);
  auto value = choc::json::parse(json);
  std::vector<Track> tracks;
  for (const auto &trackValue : value) {
    tracks.push_back(Track::deserialize(trackValue));
  }
  return tracks;
}

std::string Structures::serializeOutputMap(const OutputMap &outputMap) {
  choc::value::Value value = choc::value::createEmptyArray();
  for (const auto &mapping : outputMap) {
    value.addArrayElement(mapping.first.to_string());
    value.addArrayElement(mapping.second.to_string());
  }
  auto json = choc::json::toString(value);
  return json;
}

Structures::OutputMap
Structures::deserializeOutputMap(const std::string &data) {
  auto value = choc::json::parse(data);
  OutputMap outputMap;
  for (uint32_t i = 0; i < value.size(); i += 2) {
    auto leftChannelString = std::string(value[i].getString());
    auto leftChannel =
        std::bitset<DISTRHO_PLUGIN_NUM_OUTPUTS>(leftChannelString);
    auto rightChannelString = std::string(value[i + 1].getString());
    auto rightChannel =
        std::bitset<DISTRHO_PLUGIN_NUM_OUTPUTS>(rightChannelString);

    outputMap.push_back({leftChannel, rightChannel});
  }
  return outputMap;
}
