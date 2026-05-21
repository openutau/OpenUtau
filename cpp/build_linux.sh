#!/bin/bash

setup()
{
    sudo apt-get install gcc-arm-linux-gnueabi g++-arm-linux-gnueabi binutils-arm-linux-gnueabi
    sudo apt-get install gcc-aarch64-linux-gnu g++-aarch64-linux-gnu binutils-aarch64-linux-gnu
}

build()
{
    mkdir -p ../runtimes/linux-$1/native
    bazel build //worldline:worldline -c opt $2
    chmod +w bazel-bin/worldline/libworldline.so
    cp bazel-bin/worldline/libworldline.so ../runtimes/linux-$1/native
}

build x64 "--cpu=k8 --copt=-march=x86-64"
build arm64 "--config=ubuntu-aarch64"
