(function () {
  "use strict";
  var wv = window.chrome && window.chrome.webview;
  var state = null;
  var page = "overview";

  function send(action, args) {
    if (!wv) return;
    var m = { action: action };
    if (args) for (var k in args) m[k] = args[k];
    wv.postMessage(m);
  }
  window.__send = send;

  function $(id) { return document.getElementById(id); }

  function el(tag, cls, text) {
    var e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text != null) e.textContent = text;
    return e;
  }
  window.__el = el;

  function applyTheme(mode) {
    document.documentElement.setAttribute("data-theme", mode === "Day" ? "day" : "night");
  }
  window.__applyTheme = applyTheme;

  function showPage(name) {
    page = name;
    var btns = document.querySelectorAll(".navBtn");
    for (var i = 0; i < btns.length; i++)
      btns[i].classList.toggle("active", btns[i].getAttribute("data-page") === name);
    var pages = document.querySelectorAll(".page");
    for (var j = 0; j < pages.length; j++)
      pages[j].classList.toggle("active", pages[j].id === "page-" + name);
    render();
  }
  window.__showPage = showPage;

  function render() {
    if (!state) return;
    applyTheme(state.theme);
    $("brandVer").textContent = "v2.0.0";
    var chip = $("statusChip");
    chip.textContent = state.enabled ? "已启用" : "已禁用";
    chip.className = "chip " + (state.enabled ? "on" : "off");
    // 各页面由后续任务挂载 window.render_<page>；存在时才调用，避免破坏占位骨架
    var fn = window["render_" + page];
    if (typeof fn === "function") fn(state);
  }
  window.__render = render;

  function toast(text, kind, ms) {
    var box = $("toasts");
    if (!box) return;
    var t = el("div", "toast " + (kind || "info"), text);
    box.appendChild(t);
    setTimeout(function () { if (t.parentNode) t.parentNode.removeChild(t); }, ms || 2200);
  }
  window.__toast = toast;

  function showCloseModal() {
    var w = $("modalWrap");
    w.innerHTML = "";
    var m = el("div", "modal");
    m.appendChild(el("h4", null, "关闭窗口"));
    m.appendChild(el("div", "muted", "你想最小化到托盘，还是完全退出程序？"));
    var acts = el("div", "actions");
    var remember = el("button", "btnGhost", "记住我的选择");
    var b1 = el("button", "btn", "最小化到托盘");
    var b2 = el("button", "btnDanger", "退出");
    var remembered = false;
    remember.onclick = function () {
      remembered = !remembered;
      remember.classList.toggle("on", remembered);
    };
    b1.onclick = function () {
      w.classList.remove("show");
      send("closeConfirm", { result: "Minimize", remember: remembered });
    };
    b2.onclick = function () {
      w.classList.remove("show");
      send("closeConfirm", { result: "Exit", remember: remembered });
    };
    acts.appendChild(remember); acts.appendChild(b1); acts.appendChild(b2);
    m.appendChild(acts);
    w.appendChild(m);
    w.classList.add("show");
  }
  window.__showCloseModal = showCloseModal;

  // 通用组件
  window.__switch = function (on, onChange) {
    var b = el("button", "sw " + (on ? "on" : ""));
    b.appendChild(el("span"));
    b.onclick = function () { onChange(!on); };
    return b;
  };

  // 事件委托：导航
  document.addEventListener("click", function (e) {
    var t = e.target;
    var btn = t && t.closest ? t.closest("[data-page]") : null;
    if (btn) { showPage(btn.getAttribute("data-page")); return; }
  });

  // 窗口按钮
  $("btnTheme").onclick = function () {
    var next = document.documentElement.getAttribute("data-theme") === "day" ? "Night" : "Day";
    applyTheme(next);                 // 即时反馈；Task 10 接入 setTheme 持久化
    if (state) state.theme = next;
    send("setTheme", { mode: next });
  };
  $("btnMin").onclick = function () { send("minimizeToTray"); };
  $("btnClose").onclick = function () { send("closeRequest"); };
  $("titlebar").addEventListener("mousedown", function (e) {
    if (e.target.classList && e.target.classList.contains("winBtn")) return;
    if (e.button === 0) send("dragStart");
  });

  if (wv) {
    wv.addEventListener("message", function (e) {
      var msg = e.data;
      if (!msg || !msg.type) return;
      var data = msg.data;
      if (msg.type === "state") {
        state = data;
        window.__state = state;
        render();
      }
      else if (msg.type === "toast") { toast(data && data.text, data && data.kind, data && data.ms); }
      else if (msg.type === "askClose") { showCloseModal(); }
      else if (msg.type === "processes") {
        window.__processes = data;
        if (page === "process") render();
      }
      else if (msg.type === "history") {
        window.__history = data;
        if (page === "history") render();
      }
      else if (msg.type === "testStatus") {
        window.__testStatus = data;
        if (page === "overview") render();
      }
      else if (msg.type === "capturing") {
        window.__capturing = data;
        window.__captureRow = data && data.target === "row" ? data.index : null;
        if (data && data.target === "hotkey") window.__captureHotkey = false;
        if (page === "keys" || page === "settings") render();
      }
      else if (msg.type === "error") { toast(data && data.text, "err"); }
    });
    wv.postMessage({ action: "ready" });
  }

  // 供后续页面共享的渲染接口
  window.__ui = {
    send: send,
    el: el,
    render: render,
    showPage: showPage,
    toast: toast
  };

  render();
})();
