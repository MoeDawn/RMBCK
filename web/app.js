(function () {
  var wv = window.chrome && window.chrome.webview;
  if (!wv) return;
  wv.postMessage({ action: "ready", payload: {} });
})(window);
