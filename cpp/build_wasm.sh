#!/bin/bash

build()
{
    mkdir -p ../runtimes/wasm/native
    ${BAZEL:-bazel} build //worldline:worldline_wasm -c opt --copt=-DNDEBUG $2
    chmod +w bazel-bin/worldline/worldline.wasm
    cp bazel-bin/worldline/worldline.wasm ../runtimes/wasm/native/
    chmod +w bazel-bin/worldline/worldline.js
    cp bazel-bin/worldline/worldline.js ../runtimes/wasm/native/
}

build wasm
