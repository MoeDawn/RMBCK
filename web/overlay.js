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
    for (var i = 0; i < eggTimeouts.length; i++) {
      clearTimeout(eggTimeouts[i]);
      clearInterval(eggTimeouts[i]);   // 下载进度用的是 interval，两种都要清
    }
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

  // ---------- 彩蛋：外挂终端风格 ----------
  // 无边框全透明，靠文字自身发光悬浮在画面上；每出现一行就向宿主上报内容
  // 高度，窗口随之向下扩展，形成「边框随行数增长」的观感。
  function startEgg(items) {
    resetRoot("eggRoot");
    var box = document.createElement("div");
    box.className = "egg";
    root.appendChild(box);

    // 顶部扫描条 + 标题
    var head = document.createElement("div");
    head.className = "eggHead";
    var headText = document.createElement("span");
    headText.className = "eggHeadText";
    headText.textContent = "RMBCK // SJ-TECH";
    var headDot = document.createElement("span");
    headDot.className = "eggHeadDot";
    head.appendChild(headText);
    head.appendChild(headDot);
    box.appendChild(head);

    var scan = document.createElement("div");
    scan.className = "eggScan";
    box.appendChild(scan);

    var body = document.createElement("div");
    body.className = "eggBody";
    box.appendChild(body);

    var foot = document.createElement("div");
    foot.className = "eggFoot";
    var footBar = document.createElement("span");
    footBar.className = "eggFootBar";
    var footPct = document.createElement("span");
    footPct.className = "eggFootPct";
    footPct.textContent = "0%";
    foot.appendChild(footBar);
    foot.appendChild(footPct);
    box.appendChild(foot);

    var lines = [];
    for (var i = 0; i < items.length; i++) {
      var it = items[i];
      var d = document.createElement("div");
      d.className = "line" + (it.accent ? " accent" : "");
      var idxEl = document.createElement("span");
      idxEl.className = "eggIdx";
      idxEl.textContent = it.prefix || "";
      var textEl = document.createElement("span");
      textEl.className = "eggText";
      var markEl = document.createElement("span");
      markEl.className = "mark";
      d.appendChild(idxEl);
      d.appendChild(textEl);
      d.appendChild(markEl);
      body.appendChild(d);
      lines.push({ el: d, idxEl: idxEl, textEl: textEl, markEl: markEl, item: it });
    }

    var idx = 0;          // 当前正在打字的行
    var ch = 0;           // 当前行已打出的字符数
    var cursor = document.createElement("span");
    cursor.className = "eggCursor";

    function removeCursor() {
      if (cursor.parentNode) cursor.parentNode.removeChild(cursor);
    }

    // 把内容高度报给宿主：窗口随行数向下长高（限定最大高度避免超出屏幕）
    function syncSize() {
      requestAnimationFrame(function () {
        var h = Math.ceil(box.getBoundingClientRect().height) + 24;
        var pct = Math.min(100, Math.round((idx / lines.length) * 100));
        footPct.textContent = pct + "%";
        footBar.style.width = pct + "%";
        post({ action: "eggResize", height: h });
      });
    }

    function finishLine(L) {
      removeCursor();
      var it = L.item;
      if (it.mark) {
        later(function () {
          L.markEl.textContent = it.mark;
          L.markEl.className = "mark pop " + markClass(it.mark);
          // markThen：漏斗先转一会儿再变成另一个符号（例如 ⏳ -> √）
          if (it.markThen && it.markThenMs) {
            later(function () {
              L.markEl.textContent = it.markThen;
              L.markEl.className = "mark pop " + markClass(it.markThen);
            }, it.markThenMs);
          }
          // 下载进度：在行尾追加一个百分比进度条，从 0 走到 100
          if (it.progressMs && it.progressMs > 0) startDownloadProgress(L, it.progressMs);
          if (it.tail) {
            later(function () {
              L.markEl.textContent = it.mark + it.tail;
            }, it.tailDelayMs || 600);
          }
        }, it.markDelayMs || 260);
      }
      idx++;
      ch = 0;
      syncSize();
    }

    // 在行尾生成一个细进度条 + 百分比，走满后淡出，模拟真实下载
    function startDownloadProgress(L, totalMs) {
      var wrap = document.createElement("span");
      wrap.className = "eggDl";
      var bar = document.createElement("span");
      bar.className = "eggDlBar";
      var pct = document.createElement("span");
      pct.className = "eggDlPct";
      pct.textContent = "0%";
      wrap.appendChild(bar);
      wrap.appendChild(pct);
      L.el.appendChild(wrap);

      var steps = 20;
      var i = 0;
      var tickMs = Math.max(40, Math.round(totalMs / steps));
      var timer = setInterval(function () {
        i++;
        var p = Math.round((i / steps) * 100);
        // 前段慢、后段快，更像真实下载
        bar.style.width = p + "%";
        pct.textContent = p + "%";
        if (i >= steps) {
          clearInterval(timer);
          later(function () {
            wrap.classList.add("eggDlDone");
            later(function () { if (wrap.parentNode) wrap.parentNode.removeChild(wrap); }, 700);
          }, 260);
        }
      }, tickMs);
      // 让 clearEgg 能一并清掉
      eggTimeouts.push(timer);
      syncSize();
    }

    function step() {
      if (idx >= lines.length) {
        if (eggInterval) { clearInterval(eggInterval); eggInterval = null; }
        later(function () {
          box.classList.add("eggOut");
          later(function () {
            if (root.contains(box)) root.innerHTML = "";
            post({ action: "eggDone" });
          }, 620);
        }, 2600);
        return;
      }
      var L = lines[idx];
      var it = L.item;
      if (!L.el.classList.contains("in")) {
        L.el.classList.add("in");
        removeCursor();
        L.el.appendChild(cursor);
        syncSize();
      }
      if (it.instant) {
        L.textEl.textContent = textOf(it.text);
        if (L.idxEl) L.idxEl.textContent = "";
        finishLine(L);
        return;
      }
      // prefix 已经放在独立的索引槽里，正文不再重复拼接
      var full = (it.text || "") + (it.note ? it.note : "");
      if (ch < full.length) {
        ch++;
        L.textEl.textContent = full.substring(0, ch);
        return;
      }
      finishLine(L);
    }

    eggInterval = setInterval(step, 58);
    syncSize();
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
