// Satisfy wasm imports from deps that still reference them
// Mind you, none of these should fire during normal synthesis
// But if they do, we fail loudly or return proper error codes

// __assert_fail: C++ assertion failure: log and throw so it's visible
wasmImports.__assert_fail = function (msg, file, line, func) {
  var msgStr = UTF8ToString(msg);
  var fileStr = UTF8ToString(file);
  var funcStr = UTF8ToString(func);
  console.error(
    "Assertion failed: " + msgStr + " at " + fileStr + ":" + line + " in " + funcStr
  );
  throw new Error("Assertion failed: " + msgStr);
};

// File descriptor ops
wasmImports.fd_close = function () { return -9; };
wasmImports.fd_read = function () { return -9; };
wasmImports.fd_write = function () { return -9; };
wasmImports.fd_seek = function () { return -9; };
wasmImports.fd_fdstat_get = function () { return -9; };

// Environment — return empty environment properly
wasmImports.environ_sizes_get = function (buffer, bufSize) {
  HEAP32[buffer >> 2] = 0;       // count = 0
  HEAP32[bufSize >> 2] = 0;      // buffer size = 0
  return 0;
};

wasmImports.environ_get = function (buffer, bufSize) {
  return 0; // nothing written, success
};
