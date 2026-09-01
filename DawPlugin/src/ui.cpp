#include "DistrhoPluginInfo.h"
#include "DistrhoUI.hpp"
#include "common.hpp"
#include "dpf_widgets/generic/ResizeHandle.hpp"
#include "dpf_widgets/opengl/DearImGui/imgui.h"
#include "noto_sans/noto_sans.hpp"
#include "plugin.hpp"
#include <algorithm>
#include <string>

START_NAMESPACE_DISTRHO

// --------------------------------------------------------------------------------------------------------------------

int fontSize = 16.f;

// #ff679d
static auto themePinkColor = ImVec4(1.0f, 0.4f, 0.6f, 1.0f);
static auto themeBlueColor = ImVec4(0.3f, 0.7f, 0.9f, 1.0f);

class OpenUtauUI : public UI {
public:
  OpenUtauUI()
      : UI(DISTRHO_UI_DEFAULT_WIDTH, DISTRHO_UI_DEFAULT_HEIGHT),
        resizeHandle(this) {
    const double scaleFactor = getScaleFactor();

    if (d_isEqual(scaleFactor, 1.0)) {
      setGeometryConstraints(DISTRHO_UI_DEFAULT_WIDTH,
                             DISTRHO_UI_DEFAULT_HEIGHT);
    } else {
      const uint width = DISTRHO_UI_DEFAULT_WIDTH * scaleFactor;
      const uint height = DISTRHO_UI_DEFAULT_HEIGHT * scaleFactor;
      setGeometryConstraints(width, height);
      setSize(width, height);
    }

    if (isResizable())
      resizeHandle.hide();

    setTheme();
    setFont();
  }

  bool showDemoWindow = false;

protected:
  // ----------------------------------------------------------------------------------------------------------------
  void parameterChanged(uint32_t, float) override {}

  void stateChanged(const char *, const char *) override {}

  void onImGuiDisplay() override {
    ImGui::SetNextWindowPos(ImVec2(0, 0));
    ImGui::SetNextWindowSize(ImVec2(getWidth(), getHeight()));

    auto plugin = getPlugin();
    auto snapshot = plugin->getUiSnapshot();

    auto &style = ImGui::GetStyle();

    ImGui::Begin("OpenUtau Bridge", nullptr,
                 ImGuiWindowFlags_NoTitleBar | ImGuiWindowFlags_NoResize);

    ImGui::TextColored(themePinkColor,
#ifdef DEBUG
                       "OpenUtau Bridge v%d.%d.%d (Debug)",
#else
                       "OpenUtau Bridge v%d.%d.%d",
#endif
                       Constants::majorVersion, Constants::minorVersion,
                       Constants::patchVersion);

    ImGui::Separator();

    ImGui::Text("Plugin name:");
    ImGui::SameLine();
    ImGui::InputText("##plugin-name", nameBuffer, sizeof(nameBuffer));
    if (ImGui::IsItemDeactivatedAfterEdit()) {
      setState("name", nameBuffer);
      plugin->setPluginName(nameBuffer);
    } else if (!(ImGui::IsItemActive() &&
                 ImGui::TempInputIsActive(ImGui::GetActiveID()))) {
#if CHOC_WINDOWS
      strncpy_s(nameBuffer, snapshot.name.c_str(), sizeof(nameBuffer));
#else
      strncpy(nameBuffer, snapshot.name.c_str(), sizeof(nameBuffer));
#endif
    }

    partiallyColoredText(
        std::format("Plugin identifier: [{} ({})]", snapshot.name,
                    snapshot.port),
        themePinkColor);

    const char *connectionLabel = "Disconnected";
    ImVec4 connectionColor = style.Colors[ImGuiCol_TextDisabled];
    if (snapshot.connectionState ==
        OpenUtauPlugin::ConnectionState::Connected) {
      connectionLabel = "Connected";
      connectionColor = themePinkColor;
    } else if (snapshot.connectionState ==
               OpenUtauPlugin::ConnectionState::Error) {
      connectionLabel = "Error";
      connectionColor = themeBlueColor;
    }
    partiallyColoredText(
        std::format("Connection: [{}]", connectionLabel), connectionColor);
    if (!snapshot.lastError.empty()) {
      ImGui::TextWrapped("Last error: %s", snapshot.lastError.c_str());
    }

#ifdef DEBUG
    if (ImGui::IsKeyPressed(ImGui::GetKeyIndex(ImGuiKey_D))) {
      showDemoWindow = !showDemoWindow;
    }
    if (showDemoWindow) {
      ImGui::ShowDemoWindow(&showDemoWindow);
    }
#endif

    ImGui::Text("Last audio sync: ");
    ImGui::SameLine(0, 0);

    if (snapshot.processing) {
      ImGui::TextColored(themeBlueColor, "Processing");
    } else {
      if (snapshot.lastSync) {
        long lastSyncDuration =
            std::chrono::duration_cast<std::chrono::seconds>(
                std::chrono::system_clock::now() - *snapshot.lastSync)
                .count();
        if (lastSyncDuration > 60) {
          ImGui::Text("%ldm ago", lastSyncDuration / 60);
        } else {
          ImGui::TextColored(themePinkColor, "%lds ago", lastSyncDuration);
        }
      } else {
        ImGui::TextColored(style.Colors[ImGuiCol_TextDisabled], "N/A");
      }
    }

    if (!snapshot.tracks.empty()) {
      ImGui::Spacing();
      ImGui::TextColored(themePinkColor, "Track Mapping:");
      if (ImGui::BeginTable("##track_mapping",
                            DISTRHO_PLUGIN_NUM_OUTPUTS / 2 + 1,
                            ImGuiTableFlags_Borders)) {
        ImGui::TableSetupColumn("Track", ImGuiTableColumnFlags_NoReorder);
        for (int i = 0; i < DISTRHO_PLUGIN_NUM_OUTPUTS / 2; i++) {
          ImGui::TableSetupColumn(std::format("Ch. {}", i + 1).c_str(),
                                  ImGuiTableColumnFlags_NoReorder |
                                      ImGuiTableColumnFlags_WidthFixed |
                                      ImGuiTableColumnFlags_NoSort,
                                  style.FramePadding.x * 4 + fontSize * 2);
        }
        ImGui::TableHeadersRow();
        const auto mappingCount =
            std::min(snapshot.tracks.size(), snapshot.outputMap.size());
        for (size_t i = 0; i < mappingCount; i++) {
          ImGui::TableNextRow();
          for (int lr = 0; lr < 2; lr++) {
            ImGui::TableNextColumn();
            ImGui::Text("%s: %s", snapshot.tracks[i].name.c_str(),
                        lr == 0 ? "L" : "R");
            for (int j = 0; j < DISTRHO_PLUGIN_NUM_OUTPUTS; j += 2) {
              ImGui::TableNextColumn();
              bool leftValue;
              bool rightValue;
              if (lr == 0) {
                leftValue = snapshot.outputMap[i].first[j];
                rightValue = snapshot.outputMap[i].first[j + 1];
              } else {
                leftValue = snapshot.outputMap[i].second[j];
                rightValue = snapshot.outputMap[i].second[j + 1];
              }

              bool leftCheckboxChanged = ImGui::Checkbox(
                  std::format("##track-mapping-checkbox-{}-{}-{}", i, lr, j)
                      .c_str(),
                  &leftValue);
              if (ImGui::IsItemHovered(ImGuiHoveredFlags_AllowWhenDisabled)) {
                ImGui::SetTooltip("Channel %d, Left", j / 2 + 1);
              }
              ImGui::SameLine(0, style.ItemSpacing.x / 2.0f);
              bool rightCheckboxChanged = ImGui::Checkbox(
                  std::format("##track-mapping-checkbox-{}-{}-{}", i, lr, j + 1)
                      .c_str(),
                  &rightValue);
              if (ImGui::IsItemHovered(ImGuiHoveredFlags_AllowWhenDisabled)) {
                ImGui::SetTooltip("Channel %d, Right", j / 2 + 1);
              }
              if (leftCheckboxChanged || rightCheckboxChanged) {
                auto newOutputMap = snapshot.outputMap;
                if (lr == 0) {
                  newOutputMap[i].first[j] = leftValue;
                  newOutputMap[i].first[j + 1] = rightValue;
                } else {
                  newOutputMap[i].second[j] = leftValue;
                  newOutputMap[i].second[j + 1] = rightValue;
                }
                setState("mapping",
                         Structures::serializeOutputMap(newOutputMap).c_str());
                plugin->setOutputMap(newOutputMap);
              }
            }
          }
        }
        ImGui::EndTable();
      }
    } else {
      ImGui::Text("Please sync with OpenUtau first.");
      ImGui::Text("1. Launch OpenUtau");
      partiallyColoredText(
          "2. Click ['File'] > ['Connect to DAW...'] in OpenUtau",
          themePinkColor);
      partiallyColoredText(std::format("3. Select ['{} ({})'] in the list",
                                       snapshot.name, snapshot.port),
                           themePinkColor);
    }

    ImGui::End();
  }

  OpenUtauPlugin *getPlugin() {
    return static_cast<OpenUtauPlugin *>(getPluginInstancePointer());
  }

  // ----------------------------------------------------------------------------------------------------------------
  void setTheme() {
    ImGui::StyleColorsLight();
    ImVec4 *colors = ImGui::GetStyle().Colors;
    // #ff679d
    auto color = themePinkColor;
    auto darkColor = ImVec4(0.8f, 0.3f, 0.5f, 1.0f);
    auto lightColor = ImVec4(1.0f, 0.5f, 0.7f, 1.0f);
    colors[ImGuiCol_SliderGrab] = color;
    colors[ImGuiCol_SliderGrabActive] = darkColor;
    colors[ImGuiCol_ButtonActive] = darkColor;
    colors[ImGuiCol_SeparatorHovered] = lightColor;
    colors[ImGuiCol_TabHovered] = lightColor;
    colors[ImGuiCol_TabActive] = color;
    colors[ImGuiCol_CheckMark] = color;
    colors[ImGuiCol_PlotHistogram] = color;
    colors[ImGuiCol_PlotHistogramHovered] = darkColor;

    // #4ea6ea
    auto hoverColor = themeBlueColor;
    colors[ImGuiCol_FrameBgHovered] =
        ImVec4(hoverColor.w, hoverColor.x, hoverColor.y, 0.20f);
    colors[ImGuiCol_FrameBgActive] =
        ImVec4(hoverColor.w, hoverColor.x, hoverColor.y, 0.40f);
  }

  // TODO: Implement Chinese font properly
  // https://heistak.github.io/your-code-displays-japanese-wrong/
  void setFont() {
    const double scaleFactor = getScaleFactor();

    auto &io = ImGui::GetIO();
    ImFontConfig fc;
    fc.FontDataOwnedByAtlas = false;
    fc.OversampleH = 1;
    fc.OversampleV = 1;
    fc.PixelSnapH = true;

    ImFontGlyphRangesBuilder rangeBuilder;
    static ImVector<ImWchar> ranges;
    rangeBuilder.AddRanges(ImGui::GetIO().Fonts->GetGlyphRangesDefault());
    rangeBuilder.AddRanges(ImGui::GetIO().Fonts->GetGlyphRangesJapanese());
    rangeBuilder.AddRanges(ImGui::GetIO().Fonts->GetGlyphRangesKorean());
    rangeBuilder.AddRanges(ImGui::GetIO().Fonts->GetGlyphRangesCyrillic());
    rangeBuilder.AddRanges(ImGui::GetIO().Fonts->GetGlyphRangesVietnamese());
    rangeBuilder.AddRanges(ImGui::GetIO().Fonts->GetGlyphRangesChineseFull());
    rangeBuilder.AddRanges(ImGui::GetIO().Fonts->GetGlyphRangesThai());
    rangeBuilder.AddRanges(ImGui::GetIO().Fonts->GetGlyphRangesGreek());
    rangeBuilder.BuildRanges(&ranges);

    io.Fonts->AddFontFromMemoryTTF((void *)notoSansJpRegular,
                                   notoSansJpRegularLen, fontSize * scaleFactor,
                                   &fc, ranges.Data);
    io.Fonts->Build();
  }

  void partiallyColoredText(const std::string &text, const ImVec4 &color) {
    std::string remainingText = text;
    while (remainingText.size() > 0) {
      auto pos = remainingText.find_first_of('[');
      if (pos == std::string::npos) {
        ImGui::TextUnformatted(remainingText.c_str());
        break;
      }
      ImGui::TextUnformatted(remainingText.substr(0, pos).c_str());
      ImGui::SameLine(0, 0);
      remainingText = remainingText.substr(pos + 1);
      pos = remainingText.find_first_of(']');
      if (pos == std::string::npos) {
        ImGui::TextUnformatted(remainingText.c_str());
        break;
      }
      auto coloredText = remainingText.substr(0, pos);
      ImGui::TextColored(color, "%s", coloredText.c_str());
      remainingText = remainingText.substr(pos + 1);
      if (remainingText.size() > 0) {
        ImGui::SameLine(0, 0);
      }
    }
  }

  ResizeHandle resizeHandle;
  char nameBuffer[1024];

  DISTRHO_DECLARE_NON_COPYABLE_WITH_LEAK_DETECTOR(OpenUtauUI)
};

// --------------------------------------------------------------------------------------------------------------------

UI *createUI() { return new OpenUtauUI(); }

// --------------------------------------------------------------------------------------------------------------------

END_NAMESPACE_DISTRHO
