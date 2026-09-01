#pragma once
#include "DistrhoPluginInfo.h"
#include "choc/containers/choc_Value.h"
#include <bitset>
#include <string>
#include <vector>

namespace Utils {
std::vector<uint8_t> gunzip(const char *data, size_t size);
std::string unBase64ToString(const std::string &encoded);
std::vector<uint8_t> unBase64ToVector(const std::string &encoded);

double dbToMultiplier(double db);
} // namespace Utils

namespace Structures {
class Track {
public:
  std::string name;
  double pan;
  double volume;

  choc::value::Value serialize() const;
  static Track deserialize(const choc::value::ValueView &value);
};
using OutputMap =
    std::vector<std::pair<std::bitset<DISTRHO_PLUGIN_NUM_OUTPUTS>,
                          std::bitset<DISTRHO_PLUGIN_NUM_OUTPUTS>>>;
std::string serializeTracks(const std::vector<Track> &tracks);
std::vector<Track> deserializeTracks(const std::string &data);

std::string serializeOutputMap(const OutputMap &outputMap);
OutputMap deserializeOutputMap(const std::string &data);

} // namespace Structures

namespace Constants {
constexpr uint32_t majorVersion = 0;
constexpr uint32_t minorVersion = 1;
constexpr uint32_t patchVersion = 0;
} // namespace Constants
