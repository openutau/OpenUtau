@goto :MAIN

:BUILD
@echo Building %~1

if not exist ..\runtimes\android\%~1 mkdir ..\runtimes\android\%~1
bazel build //worldline:worldline -c opt --platforms=//:%~2
copy bazel-bin\worldline\libworldline.so ..\runtimes\android\%~1\

@EXIT /B

:MAIN
@call :BUILD armeabi-v7a armeabi-v7a
@call :BUILD arm64-v8a arm64-v8a
@call :BUILD x86 android_x86_32
@call :BUILD x86_64 android_x86_64
