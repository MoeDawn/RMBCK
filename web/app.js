(function () {
  var wv = window.chrome && window.chrome.webview;
  if (!wv) return;
  var state = null;
  wv.addEventListener("message", function (e) {
    var msg = e.data;
    if (!msg) return;
    if (msg.type === "state") {
      state = msg.data;
      document.title = "STATE:" + (msg.data.keys ? msg.data.keys.length : 0);
    }
  });
  wv.postMessage({ action: "ready", payload: {} });
})(window);
