import sys
import os
import pythonnet
import subprocess
pythonnet.load("coreclr")
import clr

def dotnet_build(type_: str = "Release"):
    cmd = ["dotnet",
           "build",
           "-c",
           type_]
    return subprocess.run(cmd, capture_output=True, text=True)

MY_DIR = os.path.dirname(os.path.abspath(__file__))
OpenUtauPath = os.path.join(MY_DIR,r"OpenUtau\bin\Debug\net8.0-windows\OpenUtau.dll")
OpenUtauCorePath = os.path.join(MY_DIR,r"OpenUtau\bin\Debug\net8.0-windows\OpenUtau.Core.dll")
OpenUtauPluginBuiltinPath = os.path.join(MY_DIR,r"OpenUtau\bin\Debug\net8.0-windows\OpenUtau.Plugin.Builtin.dll")
if not os.path.exists(OpenUtauPath):
    dotnet_build()
clr.AddReference(OpenUtauPath) # type: ignore
clr.AddReference(OpenUtauCorePath) # type: ignore
clr.AddReference(OpenUtauPluginBuiltinPath) # type: ignore
from OpenUtau import * # type: ignore
from OpenUtau.Core import * # type: ignore
from OpenUtau.Plugin.Builtin import * # type: ignore