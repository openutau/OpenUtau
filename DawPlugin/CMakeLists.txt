cmake_minimum_required(VERSION 3.24)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

set(DPF_ROOT_DIR "${CMAKE_CURRENT_SOURCE_DIR}/deps/dpf")

if(CMAKE_BUILD_TYPE STREQUAL "Debug")
  set(PROJECT_NAME openutau_daw_plugin_debug)
elseif(CMAKE_BUILD_TYPE STREQUAL "Release")
  set(PROJECT_NAME openutau_daw_plugin)
elseif(NOT CMAKE_BUILD_TYPE)
  message(FATAL_ERROR "Build type not set")
else()
  message(FATAL_ERROR "Unknown build type: ${CMAKE_BUILD_TYPE}")
endif()

set(NOTO_SANS_DIR "${CMAKE_CURRENT_SOURCE_DIR}/deps/noto_sans")
if(NOT EXISTS "${NOTO_SANS_DIR}/otf")
  file(DOWNLOAD
       "https://github.com/notofonts/noto-cjk/releases/download/Sans2.004/05_NotoSansCJK-SubsetOTF.zip"
       "${NOTO_SANS_DIR}/05_NotoSansCJK-SubsetOTF.zip")
  file(ARCHIVE_EXTRACT INPUT "${NOTO_SANS_DIR}/05_NotoSansCJK-SubsetOTF.zip"
       DESTINATION "${NOTO_SANS_DIR}/otf")
endif()
if(NOT EXISTS "${NOTO_SANS_DIR}/noto_sans.hpp")
  execute_process(COMMAND xxd -i "${NOTO_SANS_DIR}/otf/SubsetOTF/JP/NotoSansJP-Regular.otf" "${NOTO_SANS_DIR}/noto_sans.raw.hpp")
  file(READ "${NOTO_SANS_DIR}/noto_sans.raw.hpp" NOTO_SANS_HPP)
  string(REGEX REPLACE "unsigned char .+\\[\\]" "const unsigned char notoSansJpRegular[]" NOTO_SANS_HPP "${NOTO_SANS_HPP}")
  string(REGEX REPLACE "unsigned int .+_len" "const unsigned int notoSansJpRegularLen" NOTO_SANS_HPP "${NOTO_SANS_HPP}")
  file(WRITE "${NOTO_SANS_DIR}/noto_sans.hpp" "${NOTO_SANS_HPP}")
endif()

include(./deps/dpf/cmake/DPF-plugin.cmake)
project(${PROJECT_NAME})

# MSVC only:
# - Use UTF-8 code page
# - Make asio use Windows 10 APIs
# - Enable IME
if(MSVC)
  add_compile_options("/utf-8")
  add_definitions(-D_WIN32_WINNT=0x0A00)
  add_definitions(-DIMGUI_ENABLE_WIN32_DEFAULT_IME_FUNCTIONS)
endif()

# macOS only:
# - Enable libc++ C++20 stop_token/jthread support
# - Use -Wno-implicit-function-declaration for zlib
if(APPLE)
  add_compile_options($<$<COMPILE_LANGUAGE:CXX>:-fexperimental-library>)
  add_link_options(-fexperimental-library)
  add_compile_options("-Wno-implicit-function-declaration")
endif()

if(CMAKE_BUILD_TYPE STREQUAL "Debug")
  add_definitions(-DDEBUG)
  add_definitions(-DDPF_DEBUG)
elseif(CMAKE_BUILD_TYPE STREQUAL "Release")
  # nothing atm
endif()

# Disable DGL's default font
add_definitions(-DDGL_NO_SHARED_RESOURCES=1)

# DPF does not currently recognize MSVC's uppercase "ARM64" architecture ID.
# Use the package architecture name required by the VST3 bundle layout.
if(MSVC AND MSVC_CXX_ARCHITECTURE_ID STREQUAL "ARM64")
  set(DPF_VST3_ARCHITECTURE "arm64")
endif()

dpf_add_plugin(
  ${PROJECT_NAME}
  TARGETS
    vst3 au
  UI_TYPE
    opengl
  USE_FILE_BROWSER
  FILES_COMMON
    src/common.cpp
  FILES_DSP
    src/plugin.cpp
  FILES_UI
    src/ui.cpp
    deps/dpf_widgets/opengl/DearImGui.cpp)

# Including other libraries before DPF causes "find_library" to fail with infinite
# recursion, so we include it after DPF
set(ZLIB_BUILD_EXAMPLES OFF)
set(RENAME_ZCONF OFF)
add_subdirectory(deps/zlib)

target_include_directories(
  ${PROJECT_NAME}
  PUBLIC "src"
         "deps"
         "deps/choc"
         "deps/dpf/dgl"
         "deps/dpf/distrho"
         "deps/asio/asio/include"
         "deps/dpf_widgets"
         "deps/dpf_widgets/opengl"
         "deps/uuid-v4"
         "deps/zlib"
         "deps/yamc/include"
         "deps/gzip-hpp/include")

target_link_libraries(${PROJECT_NAME} PRIVATE zlibstatic)
