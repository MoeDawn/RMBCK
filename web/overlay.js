/*
 * RMBCK 透明悬浮窗页面：Toast / HUD / 彩蛋动画
 * 页面初始为全透明；没有收到任何消息时保持空白。
 * 宿主消息：{"kind":"toast|hud|hud-hide|egg|theme","data":{...}}
 * （theme 同时兼容顶层 mode 字段）
 */
(function () {
  "use strict";

  var wv = window.chrome && window.chrome.webview;
  var root = document.getElementById("root");
  var eggInterval = null;
  var eggTimeouts = [];

  function post(obj) {
    try { if (wv) wv.postMessage(obj); } catch (e) { }
  }

  function clearEgg() {
    if (eggInterval) { clearInterval(eggInterval); eggInterval = null; }
    for (var i = 0; i < eggTimeouts.length; i++) clearTimeout(eggTimeouts[i]);
    eggTimeouts = [];
  }

  function later(fn, ms) {
    var id = setTimeout(function () {
      for (var i = 0; i < eggTimeouts.length; i++) {
        if (eggTimeouts[i] === id) { eggTimeouts.splice(i, 1); break; }
      }
      fn();
    }, ms);
    eggTimeouts.push(id);
    return id;
  }

  function resetRoot(cls) {
    clearEgg();
    root.innerHTML = "";
    root.className = cls || "";
  }

  function textOf(v) { return v == null ? "" : String(v); }

  function render(msg) {
    var kind = msg.kind;
    var data = msg.data || {};
    if (kind === "toast") {
      // 反馈流程使用居中大卡片；普通 Toast 仍是左上角紧凑样式
      resetRoot(data.center ? "centerRoot" : "");
      var c = document.createElement("div");
      c.className = "card" + (data.kind ? " " + data.kind : "");
      c.textContent = textOf(data.text);
      root.appendChild(c);
    } else if (kind === "hud") {
      resetRoot("hudRoot");
      var h = document.createElement("div");
      h.className = "hud";
      h.textContent = textOf(data.text);
      root.appendChild(h);
    } else if (kind === "hud-hide" || kind === "clear") {
      resetRoot("");
    } else if (kind === "egg") {
      startEgg(data.items || []);
    }
  }

  function markClass(mark) {
    if (mark === "√") return "ok";
    if (mark === "❌") return "err";
    return "warn";
  }

  function startEgg(items) {
    resetRoot("");
    var box = document.createElement("div");
    box.className = "card egg";
    root.appendChild(box);

    var lines = [];
    for (var i = 0; i < items.length; i++) {
      var it = items[i];
      var d = document.createElement("div");
      d.className = "line" + (it.accent ? " accent" : "");
      var textEl = document.createElement("span");
      d.appendChild(textEl);
      var markEl = document.createElement("span");
      markEl.className = "mark";
      d.appendChild(markEl);
      box.appendChild(d);
      lines.push({ el: d, textEl: textEl, markEl: markEl, item: it });
    }

    var idx = 0;
    var ch = 0;
    var cursor = document.createElement("span");
    cursor.className = "cursor";

    function removeCursor() {
      if (cursor.parentNode) cursor.parentNode.removeChild(cursor);
    }

    function finishLine(L) {
      removeCursor();
      var it = L.item;
      if (it.mark) {
        later(function () {
          L.markEl.textContent = it.mark;
          L.markEl.className = "mark " + markClass(it.mark);
          if (it.tail) {
            later(function () {
              L.markEl.textContent = it.mark + it.tail;
            }, it.tailDelayMs || 600);
          }
        }, it.markDelayMs || 220);
      }
      idx++;
      ch = 0;
    }

    function step() {
      if (idx >= lines.length) {
        if (eggInterval) { clearInterval(eggInterval); eggInterval = null; }
        later(function () {
          box.style.transition = "opacity .45s";
          box.style.opacity = "0";
          later(function () {
            if (root.contains(box)) root.innerHTML = "";
            post({ action: "eggDone" });
          }, 500);
        }, 2200);
        return;
      }
      var L = lines[idx];
      var it = L.item;
      if (!L.el.classList.contains("in")) {
        L.el.classList.add("in");
        removeCursor();
        L.el.appendChild(cursor);
      }
      if (it.instant) {
        L.textEl.textContent = textOf(it.text);
        finishLine(L);
        return;
      }
      var full = (it.prefix || "") + (it.text || "") + (it.note ? it.note : "");
      if (ch < full.length) {
        ch++;
        L.textEl.textContent = full.substring(0, ch);
        return;
      }
      finishLine(L);
    }

    eggInterval = setInterval(step, 26);
  }

  function applyTheme(mode) {
    document.documentElement.setAttribute("data-theme", mode === "Day" ? "day" : "night");
  }

  if (wv) {
    wv.addEventListener("message", function (e) {
      var m = e.data;
      if (!m || !m.kind) return;
      if (m.kind === "theme") {
        applyTheme(m.mode || (m.data && m.data.mode));
        return;
      }
      render(m);
    });
    post({ action: "overlayReady" });
  }
})();
