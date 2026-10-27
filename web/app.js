(function () {
  "use strict";
  var wv = window.chrome && window.chrome.webview;
  var state = null;
  var page = "overview";
  window.__autoBusy = false;   // 开机自启切换进行中（设置页禁用开关并忽略连击）

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
    // 捕获是全局监听：离开当前页面前必须撤销，避免在不可见的行上静默改写键位
    if (window.__capturing && window.__capturing.active) send("cancelCapture", {});
    page = name;
    var btns = document.querySelectorAll(".navBtn");
    for (var i = 0; i < btns.length; i++)
      btns[i].classList.toggle("active", btns[i].getAttribute("data-page") === name);
    var pages = document.querySelectorAll(".page");
    for (var j = 0; j < pages.length; j++)
      pages[j].classList.toggle("active", pages[j].id === "page-" + name);
    render();
    // 日志只存在内存里：每次进入页面主动拉一次，避免显示上次打开时的旧列表
    if (name === "history") send("showHistory", {});
  }
  window.__showPage = showPage;

  function render() {
    if (!state) return;
    applyTheme(state.theme);
    var chip = $("statusChip");
    chip.textContent = state.enabled ? "已启用" : "已禁用";
    chip.className = "chip " + (state.enabled ? "on" : "off");
    // 各页面由后续任务挂载 window.render_<page>；存在时才调用，避免破坏占位骨架
    var fn = window["render_" + page];
    if (typeof fn === "function") fn(state);
    if (typeof window.__syncSecretDot === "function") window.__syncSecretDot(state);
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

  // 反馈流程的进度遮罩：直接画在主页面里，主页面早已加载完成，
  // 不像透明悬浮窗那样要等 2 秒冷启动——用户提交后立刻能看到反馈动画。
  var progressEl = null;
  function showProgress(d) {
    if (!d.visible) {
      if (progressEl && progressEl.parentNode) progressEl.parentNode.removeChild(progressEl);
      progressEl = null;
      return;
    }
    if (!progressEl) {
      progressEl = el("div", "progressWrap");
      var card = el("div", "progressCard");
      progressEl.appendChild(card);
      progressEl.appendChild(el("div", "progressBarOuter")).appendChild(el("div", "progressBarFill"));
      document.body.appendChild(progressEl);
    }
    var cardEl = progressEl.querySelector(".progressCard");
    if (cardEl) cardEl.textContent = d.text || "";
    var fill = progressEl.querySelector(".progressBarFill");
    if (fill) {
      var pct = typeof d.percent === "number" ? Math.max(0, Math.min(100, d.percent)) : 100;
      fill.style.width = pct + "%";
      fill.style.transition = d.animate ? "width .9s ease" : "none";
    }
  }
  window.__showProgress = showProgress;

  function showCloseModal() {
    var w = $("modalWrap");
    w.innerHTML = "";
    var m = el("div", "modal");
    m.setAttribute("role", "dialog");
    m.setAttribute("aria-modal", "true");
    m.setAttribute("aria-labelledby", "closeModalTitle");
    var title = el("h4", null, "关闭窗口");
    title.id = "closeModalTitle";
    m.appendChild(title);
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
    b1.focus();
  }
  window.__showCloseModal = showCloseModal;

  // 报错反馈：收集问题描述与分类后交给 C# 走 v1 的“提交→占卜→全屏图片”流程
  function showFeedback() {
    var w = $("modalWrap");
    w.innerHTML = "";
    var m = el("div", "modal");
    m.setAttribute("role", "dialog");
    m.setAttribute("aria-modal", "true");
    m.setAttribute("aria-labelledby", "feedbackModalTitle");

    var title = el("h4", null, "报错反馈");
    title.id = "feedbackModalTitle";
    m.appendChild(title);
    m.appendChild(el("div", "muted", "请描述你遇到的问题。"));

    var t = el("textarea");
    t.placeholder = "问题描述…";
    t.rows = 4;
    t.style.width = "100%";
    t.style.marginTop = "10px";
    t.style.borderRadius = "8px";
    t.style.border = "1px solid var(--line)";
    t.style.background = "var(--cardHi)";
    t.style.color = "var(--text)";
    t.style.font = "inherit";
    t.style.padding = "8px 10px";
    t.style.resize = "vertical";
    m.appendChild(t);

    var type = el("select");
    ["闪退", "按键无效", "界面异常", "其他"].forEach(function (v) {
      var o = el("option", null, v);
      o.value = v;
      type.appendChild(o);
    });
    type.style.marginTop = "8px";
    type.style.width = "100%";
    m.appendChild(type);

    var acts = el("div", "actions");
    var bCancel = el("button", "btnGhost", "取消");
    var bOk = el("button", "btn", "提交");
    bCancel.onclick = function () { w.classList.remove("show"); };
    bOk.onclick = function () {
      w.classList.remove("show");
      send("submitFeedback", { text: t.value, type: type.value });
    };
    acts.appendChild(bCancel);
    acts.appendChild(bOk);
    m.appendChild(acts);
    w.appendChild(m);
    w.classList.add("show");
    t.focus();
  }
  window.__showFeedback = showFeedback;

  // 隐蔽开关（沿用 v1）：右下角 8px 圆点，切换彩蛋概率 1/20 ↔ 3/5。
  // 关闭态几乎不可见；开启后变强调色并出现指针，只有知道位置的人才点得到。
  var secretDot = $("secretDot");
  if (secretDot) {
    secretDot.onclick = function () {
      var cur = !!(window.__state && window.__state.eggBoost);
      send("setSetting", { key: "eggBoost", value: !cur });
      // 乐观切换，等 C# 的 state 回推确认
      secretDot.classList.toggle("on", !cur);
      secretDot.style.cursor = !cur ? "pointer" : "default";
    };
  }
  window.__syncSecretDot = function (s) {
    if (!secretDot || !s) return;
    secretDot.classList.toggle("on", !!s.eggBoost);
    secretDot.style.cursor = s.eggBoost ? "pointer" : "default";
  };

  // 通用组件
  window.__switch = function (on, onChange) {
    var b = el("button", "sw " + (on ? "on" : ""));
    b.appendChild(el("span"));
    b.onclick = function () { onChange(!on); };
    return b;
  };

  // 页面重建前记录滚动位置、焦点 id 与文本/数字输入选区，重建后恢复。
  // 只有“状态确实变化”的页面才重建，避免无谓地销毁用户正在操作的控件。
  function capturePageState(p) {
    var a = document.activeElement;
    var st = { id: "", start: null, end: null, scrollTop: p ? p.scrollTop : 0 };
    if (a && a.id) st.id = a.id;
    if (a && (a.tagName === "INPUT" || a.tagName === "TEXTAREA")) {
      try {
        if (typeof a.selectionStart === "number") {
          st.start = a.selectionStart;
          st.end = a.selectionEnd;
        }
      } catch (e) { }
    }
    return st;
  }

  function restorePageState(p, st) {
    if (!p || !st) return;
    p.scrollTop = st.scrollTop || 0;
    if (!st.id) return;
    var a = document.getElementById(st.id);
    if (!a) return;
    try { a.focus({ preventScroll: true }); } catch (e) {
      try { a.focus(); } catch (e2) { }
    }
    if (st.start != null) {
      try { a.setSelectionRange(st.start, st.end); } catch (e3) { }
    }
  }

  // ---------- 按键页 ----------
  var MOD_LABEL = { 1: "Ctrl", 2: "Shift", 4: "Alt", 8: "Win" };
  var MODE_LABEL = { Hold: "保持", TapOnce: "点按·单次", TapRepeat: "点按·连发" };

  function isCapturingRow(index) {
    var c = window.__capturing;
    if (c) return !!(c.active && c.target === "row" && c.index === index);
    return window.__captureRow === index;
  }

  function describeKey(k) {
    var mods = [];
    [1, 2, 4, 8].forEach(function (bit) { if (k.mods & bit) mods.push(MOD_LABEL[bit]); });
    var name = k.name || "（未设置）";
    return (mods.length ? mods.join("+") + "+" : "") + name;
  }

  function keyHeader() {
    var h = el("div", "keyRow keyHead");
    ["启用", "键位", "模式", "延迟ms", "连发ms"].forEach(function (t) { h.appendChild(el("span", null, t)); });
    h.appendChild(el("span", null, ""));
    return h;
  }

  function keyRow(k) {
    var r = el("div", "keyRow");

    r.appendChild(window.__switch(k.enabled, function (v) {
      send("setKey", { index: k.index, field: "enabled", value: v });
    }));

    var cell = el("div", "keyName");
    if (isCapturingRow(k.index)) {
      cell.appendChild(el("span", "capturing", "请按下组合键…"));
    } else {
      var b = el("button", "keyBtn", describeKey(k));
      b.onclick = function () { send("captureKey", { target: "row", index: k.index }); };
      cell.appendChild(b);
    }
    r.appendChild(cell);

    var sel = el("select");
    ["Hold", "TapOnce", "TapRepeat"].forEach(function (m) {
      var o = el("option", null, MODE_LABEL[m]);
      o.value = m;
      if (m === k.mode) o.selected = true;
      sel.appendChild(o);
    });
    sel.onchange = function () { send("setKey", { index: k.index, field: "mode", value: sel.value }); };
    r.appendChild(sel);

    var delay = el("input");
    delay.type = "number";
    delay.min = "0";
    delay.max = "2000";
    delay.value = k.delay;
    delay.title = "到达检测时间后再延迟该毫秒数触发本行";
    delay.onchange = function () {
      var v = parseInt(delay.value, 10);
      if (isNaN(v)) v = 0;
      send("setKey", { index: k.index, field: "delay", value: v });
    };
    r.appendChild(delay);

    var rep = el("input");
    rep.type = "number";
    rep.min = "10";
    rep.max = "2000";
    rep.value = k.repeat;
    rep.disabled = k.mode !== "TapRepeat";
    rep.onchange = function () {
      var v = parseInt(rep.value, 10);
      if (isNaN(v)) v = 100;
      send("setKey", { index: k.index, field: "repeat", value: v });
    };
    r.appendChild(rep);

    var del = el("button", "iconBtn", "✕");
    del.title = "删除此行";
    del.onclick = function () { send("removeKey", { index: k.index }); };
    r.appendChild(del);
    return r;
  }

  var _pointerDownAt = 0;
  var _pendingKeysRender = false;

  function keysSignature(s) {
    var keys = s.keys || [];
    var rows = [];
    for (var i = 0; i < keys.length; i++) {
      var k = keys[i];
      rows.push([k.index, k.enabled, k.name, k.mode, k.mods, k.repeat, k.delay]);
    }
    return JSON.stringify(rows);
  }

  function captureSignature() {
    var c = window.__capturing;
    if (c) return c.active ? (c.target || "") + ":" + (c.index == null ? "" : c.index) : "";
    return window.__captureRow == null ? "" : "row:" + window.__captureRow;
  }

  document.addEventListener("mousedown", function (e) {
    if (e.button !== 0 || page !== "keys") return;
    if (e.target && e.target.closest && e.target.closest("#page-keys"))
      _pointerDownAt = Date.now();
  }, true);
  document.addEventListener("mouseup", function (e) {
    if (e.button !== 0) return;
    var had = _pointerDownAt > 0;
    _pointerDownAt = 0;
    if (had && _pendingKeysRender) {
      _pendingKeysRender = false;
      // 让本轮 mouseup/click 先处理完，再重建，避免销毁用户正在点击的按钮
      setTimeout(function () { if (page === "keys") render(); }, 30);
    }
  }, true);
  window.addEventListener("blur", function () {
    _pointerDownAt = 0;
    if (_pendingKeysRender) {
      _pendingKeysRender = false;
      if (page === "keys") render();
    }
  });

  window.render_keys = function (s) {
    var p = document.getElementById("page-keys");
    if (!p) return;

    // 状态没变（含捕获指示）就不重建，避免在用户点击途中销毁控件
    var sig = keysSignature(s) + "||" + captureSignature();
    if (window.__keysRenderSig === sig) return;
    if (_pointerDownAt > 0 && Date.now() - _pointerDownAt < 2000) {
      _pendingKeysRender = true;
      return;
    }
    _pendingKeysRender = false;

    p.innerHTML = "";

    var conflicts = s.conflicts || [];
    if (conflicts.length) {
      var warn = el("div", "card warnCard");
      warn.appendChild(el("h3", null, "⚠ 检测到重复键位"));
      var names = conflicts.map(function (c) {
        return "第 " + (c.a + 1) + " 行与第 " + (c.b + 1) + " 行";
      });
      warn.appendChild(el("div", "muted", names.join("；") + "键位相同，可能重复触发。"));
      p.appendChild(warn);
    }

    var card = el("div", "card");
    card.appendChild(el("h3", null, "按键列表"));
    var list = el("div", "keyList");
    list.appendChild(keyHeader());
    var keys = s.keys || [];
    keys.forEach(function (k) { list.appendChild(keyRow(k)); });
    if (!keys.length) list.appendChild(el("div", "muted empty", "还没有按键，点下方「添加按键」"));
    card.appendChild(list);

    var add = el("button", "btn addKey", "＋ 添加按键");
    add.onclick = function () { send("addKey", {}); };
    card.appendChild(add);
    p.appendChild(card);
    window.__keysRenderSig = sig;
  };

  // ---------- 概览页 ----------
  function overviewSignature(s) {
    return JSON.stringify([
      s.enabled, s.profiles || [], s.profile, s.longPressMs, s.randomJitter,
      s.hotkey, !!window.__testing
    ]);
  }

  window.render_overview = function (s) {
    var p = document.getElementById("page-overview");
    if (!p) return;
    var sig = overviewSignature(s);
    if (window.__overviewRenderSig === sig) return;
    var pageState = capturePageState(p);
    p.innerHTML = "";

    // 总开关 + 方案
    var c1 = el("div", "card");
    c1.appendChild(el("h3", null, "联动"));
    var row1 = el("div", "row");
    var enabledSw = window.__switch(s.enabled, function (v) { send("toggleEnabled", { enabled: v }); });
    enabledSw.id = "overviewEnabled";
    row1.appendChild(enabledSw);
    row1.appendChild(el("span", "muted", s.enabled ? "当前联动状态：启用" : "当前联动状态：禁用"));
    row1.appendChild(el("span", "spacer"));
    var sel = el("select");
    sel.id = "profileSelect";
    var profiles = s.profiles || [];
    var hasCurrent = false;
    profiles.forEach(function (n) {
      var o = el("option", null, n);
      o.value = n;
      if (n === s.profile) { o.selected = true; hasCurrent = true; }
      sel.appendChild(o);
    });
    // 方案文件缺失时仍然展示当前方案，避免下拉框空白
    if (!hasCurrent && s.profile) {
      var cur = el("option", null, s.profile);
      cur.value = s.profile;
      cur.selected = true;
      sel.appendChild(cur);
    }
    sel.onchange = function () { send("setProfile", { name: sel.value }); };
    row1.appendChild(sel);
    var bNew = el("button", "btn", "＋ 新建");
    bNew.id = "newProfileBtn";
    bNew.onclick = function () { promptProfileName(); };
    var bDel = el("button", "btnGhost", "－ 删除");
    bDel.id = "deleteProfileBtn";
    bDel.onclick = function () { confirmDeleteProfile(s.profile); };
    row1.appendChild(bNew);
    row1.appendChild(bDel);
    c1.appendChild(row1);
    c1.appendChild(el("div", "muted", "全局热键 " + s.hotkey + " 可随时切换联动开关（游戏内生效）"));
    p.appendChild(c1);

    // 阈值 + 随机化
    var c2 = el("div", "card");
    c2.appendChild(el("h3", null, "触发设置"));
    var row2 = el("div", "row");
    row2.appendChild(el("span", "muted", "长按检测时间"));
    var rng = el("input");
    rng.type = "range";
    rng.id = "longPressRange";
    rng.min = "100";
    rng.max = "2000";
    rng.step = "10";
    rng.value = s.longPressMs;
    rng.style.width = "260px";
    var num = el("input");
    num.type = "number";
    num.id = "longPressNumber";
    num.min = "100";
    num.max = "2000";
    num.value = s.longPressMs;
    num.style.width = "74px";
    var commit = function (v) {
      v = Math.max(100, Math.min(2000, parseInt(v, 10) || 300));
      rng.value = v;
      num.value = v;
      send("setSetting", { key: "longPressMs", value: v });
    };
    rng.oninput = function () { num.value = rng.value; };
    rng.onchange = function () { commit(rng.value); };
    num.onchange = function () { commit(num.value); };
    row2.appendChild(rng);
    row2.appendChild(num);
    row2.appendChild(el("span", "muted", "ms"));
    row2.appendChild(el("span", "spacer"));
    row2.appendChild(el("span", "muted", "随机化防检测"));
    var jitterSw = window.__switch(s.randomJitter, function (v) { send("setSetting", { key: "randomJitter", value: v }); });
    jitterSw.id = "randomJitterSwitch";
    row2.appendChild(jitterSw);
    c2.appendChild(row2);
    p.appendChild(c2);

    // 测试面板
    var c3 = el("div", "card");
    c3.appendChild(el("h3", null, "测试"));
    var row3 = el("div", "row");
    var testBtn = el("button", "btn", window.__testing ? "停止测试" : "开始测试");
    testBtn.id = "testBtn";
    testBtn.onclick = function () {
      window.__testing = !window.__testing;
      send(window.__testing ? "startTest" : "stopTest");
      render();
    };
    row3.appendChild(testBtn);
    var st = window.__testStatus || {};
    var testDown = el("span", "muted", st.down ? "右键：按下" : "右键：释放");
    testDown.id = "testDown";
    row3.appendChild(testDown);
    c3.appendChild(row3);
    var lines = el("div", "testLines");
    var testKeys = el("div", "muted", "触发键位：" + (st.keys || "无"));
    testKeys.id = "testKeys";
    var testPhysical = el("div", "muted", "实际按下：" + (st.physical || "无"));
    testPhysical.id = "testPhysical";
    var testTarget = el("div", "muted", "目标进程：" + (st.target || "未选择（全局生效）"));
    testTarget.id = "testTarget";
    lines.appendChild(testKeys);
    lines.appendChild(testPhysical);
    lines.appendChild(testTarget);
    c3.appendChild(lines);
    p.appendChild(c3);
    restorePageState(p, pageState);
    window.__overviewRenderSig = sig;
  };

  // testStatus 每 100ms 到达一次：只更新文本，不重建页面（否则按钮/滑块会被销毁）
  window.updateTestPanel = function (data) {
    var down = $("testDown"), keys = $("testKeys"), phys = $("testPhysical"), target = $("testTarget");
    if (!down && !keys && !phys && !target) return;
    var st = data || {};
    if (down) down.textContent = st.down ? "右键：按下" : "右键：释放";
    if (keys) keys.textContent = "触发键位：" + (st.keys || "无");
    if (phys) phys.textContent = "实际按下：" + (st.physical || "无");
    if (target) target.textContent = "目标进程：" + (st.target || "未选择（全局生效）");
    var btn = $("testBtn");
    if (btn) btn.textContent = window.__testing ? "停止测试" : "开始测试";
  };

  // ---------- 进程页 ----------
  // 搜索在本地过滤已拉取的数组，不向 C# 发请求；查询串与加载态挂在 window 上，
  // 这样 state/processes 推送触发的重绘不会清掉用户输入或“正在枚举…”提示。
  function processSignature(s) {
    return JSON.stringify([
      s.targetProcess || "", window.__procQuery || "",
      !!window.__procLoading, !!window.__procFetched, window.__processes || []
    ]);
  }

  window.render_process = function (s) {
    var p = document.getElementById("page-process");
    if (!p) return;
    var sig = processSignature(s);
    if (window.__processRenderSig === sig) return;

    var pageState = capturePageState(p);
    var prevList = document.getElementById("procListBox");
    var listScroll = prevList ? prevList.scrollTop : 0;

    var prev = document.getElementById("procSearch");
    var hadFocus = !!prev && document.activeElement === prev;
    var caret = hadFocus ? prev.selectionStart : 0;

    p.innerHTML = "";

    var card = el("div", "card");
    card.appendChild(el("h3", null, "联动目标进程"));
    card.appendChild(el("div", "muted", "选中后仅当该进程窗口在前台时才触发联动；未选择则全局生效。"));

    var row = el("div", "row");
    var search = el("input");
    search.type = "text";
    search.id = "procSearch";
    search.placeholder = "搜索进程名…";
    search.setAttribute("aria-label", "搜索进程名或窗口标题");
    search.style.flex = "1";
    search.value = window.__procQuery || "";
    var refresh = el("button", "btn", "刷新");
    refresh.id = "procRefreshBtn";
    refresh.disabled = !!window.__procLoading;
    var clear = el("button", "btnGhost", "清除限制");
    clear.id = "procClearBtn";
    row.appendChild(search); row.appendChild(refresh); row.appendChild(clear);
    card.appendChild(row);

    var cur = el("div", "muted currentTarget",
      "当前：" + (s.targetProcess || "未限制（全局生效）"));
    cur.id = "procCurrent";
    card.appendChild(cur);

    var listBox = el("div", "procList");
    listBox.id = "procListBox";
    card.appendChild(listBox);

    function paint() {
      listBox.innerHTML = "";
      var procs = window.__processes || [];
      var loading = !!window.__procLoading;
      var fetched = !!window.__procFetched;
      var q = (window.__procQuery || "").toLowerCase();
      if (loading) listBox.appendChild(el("div", "muted procLoading", "正在枚举…"));
      var n = 0;
      procs.forEach(function (it) {
        var name = it && it.name != null ? String(it.name) : "";
        var title = it && it.title != null ? String(it.title) : "";
        if (q && name.toLowerCase().indexOf(q) < 0 && title.toLowerCase().indexOf(q) < 0) return;
        n++;
        var item = el("div", "procItem" + (name === s.targetProcess ? " selected" : ""));
        item.appendChild(el("b", null, name));
        item.appendChild(el("span", "muted", title));
        item.onclick = function () { send("selectProcess", { name: name }); };
        listBox.appendChild(item);
      });
      if (!n && !loading) {
        listBox.appendChild(el("div", "muted empty",
          !fetched ? "点「刷新」加载进程列表" : (q ? "无匹配结果" : "无可见窗口进程")));
      }
    }

    search.oninput = function () {
      window.__procQuery = search.value;
      paint();
      // 本地过滤已生效：同步签名，避免下一次无关 state 推送重建并打断输入
      window.__processRenderSig = processSignature(s);
    };
    refresh.onclick = function () {
      window.__procLoading = true;
      send("refreshProcesses", {});
      render();   // 立即进入禁用/正在枚举状态
    };
    clear.onclick = function () { send("selectProcess", { name: "" }); };

    p.appendChild(card);
    paint();
    // 重建只换 DOM：恢复列表与页面滚动，选进程后不会跳回顶部
    listBox.scrollTop = listScroll;
    restorePageState(p, pageState);

    if (hadFocus) {
      search.focus();
      try { search.setSelectionRange(caret, caret); } catch (e) { }
    }
    window.__processRenderSig = sig;
  };

  // ---------- 日志页 ----------
  // 状态推送会重绘整页：重建前后保存/恢复列表滚动位置，避免用户翻看旧记录时被拉回顶部
  window.render_history = function (s) {
    var p = document.getElementById("page-history");
    if (!p) return;
    var prevList = p.querySelector(".histList");
    var scrollTop = prevList ? prevList.scrollTop : 0;

    p.innerHTML = "";
    var items = window.__history || [];
    var card = el("div", "card");
    card.appendChild(el("h3", null, "触发日志（仅本次运行）"));
    var row = el("div", "row");
    var exp = el("button", "btn", "导出 CSV");
    var clr = el("button", "btnGhost", "清空");
    exp.disabled = !items.length;   // 空日志没有可导出的内容
    exp.onclick = function () { send("exportHistory", {}); };
    clr.onclick = function () { send("clearHistory", {}); };
    row.appendChild(exp); row.appendChild(clr);
    row.appendChild(el("span", "muted", "仅统计本次运行期间触发的记录，不写入磁盘"));
    card.appendChild(row);

    var list = el("div", "histList");
    if (!items.length) list.appendChild(el("div", "muted empty", "暂无记录"));
    items.forEach(function (it) {
      var r = el("div", "histRow");
      var time = el("span", "histTime", it.time);
      time.title = (it.date || "") + " " + (it.time || "");
      var keys = el("span", "histKeys", it.keys);
      keys.title = it.keys || "";
      var proc = el("span", "muted histProc", it.proc || "—");
      proc.title = it.proc || "";
      r.appendChild(time);
      r.appendChild(keys);
      r.appendChild(proc);
      r.appendChild(el("span", "histMode", it.mode));
      list.appendChild(r);
    });
    card.appendChild(list);
    p.appendChild(card);
    list.scrollTop = scrollTop;
  };

  // ---------- 设置页 ----------
  function hotkeyCapturing() {
    var c = window.__capturing;
    return !!(c && c.active && c.target === "hotkey");
  }

  function settingSwitch(key, on, label) {
    var box = el("label", "modBox");
    var sw = window.__switch(on, function (v) {
      send("setSetting", { key: key, value: v });
    });
    sw.id = "setting_" + key;
    box.appendChild(sw);
    box.appendChild(el("span", null, label));
    return box;
  }

  function settingsSignature(s) {
    return JSON.stringify([
      s.hotkey, s.autostart, !!window.__autoBusy,
      s.blockCtrl, s.blockShift, s.blockAlt, s.blockWin,
      s.suppressToast, s.suppressEgg, s.suppressHud, s.suppressHotkeyToast,
      s.theme, s.configFolder,
      hotkeyCapturing() || !!window.__captureHotkey
    ]);
  }

  window.render_settings = function (s) {
    var p = document.getElementById("page-settings");
    if (!p) return;
    var sig = settingsSignature(s);
    if (window.__settingsRenderSig === sig) return;
    var pageState = capturePageState(p);
    p.innerHTML = "";

    var busy = !!window.__autoBusy;

    // 热键与启动
    var c1 = el("div", "card");
    c1.appendChild(el("h3", null, "热键与启动"));
    var r1 = el("div", "row");
    r1.appendChild(el("span", "muted", "全局热键"));
    var capturing = hotkeyCapturing() || !!window.__captureHotkey;
    var hk = el("button", "hotkeyBtn" + (capturing ? " capturing" : ""),
      capturing ? "请按下组合键…" : (s.hotkey || "未设置"));
    hk.id = "hotkeyBtn";
    hk.type = "button";
    hk.title = capturing ? "按 Esc 取消" : "点击后按下组合键（须包含 Ctrl/Shift/Alt/Win）";
    hk.onclick = function () {
      if (capturing) {
        window.__captureHotkey = false;
        send("cancelCapture", {});
        render();
        return;
      }
      window.__captureHotkey = true;
      send("captureKey", { target: "hotkey" });
      render();
      // 后端可能拒绝开始捕获（如键盘钩子不可用）：超时后恢复按钮，避免一直停留在等待态
      setTimeout(function () {
        if (window.__captureHotkey && !hotkeyCapturing()) {
          window.__captureHotkey = false;
          if (page === "settings") render();
        }
      }, 800);
    };
    r1.appendChild(hk);
    r1.appendChild(el("span", "spacer"));
    r1.appendChild(el("span", "muted", "开机自启动"));
    var auto = window.__switch(s.autostart, function (v) {
      if (window.__autoBusy) return;
      window.__autoBusy = true;
      render();
      send("setAutostart", { on: v });
    });
    auto.id = "autostartSwitch";
    auto.type = "button";
    if (busy) {
      auto.disabled = true;
      auto.classList.add("busy");
      auto.title = "正在处理…";
    }
    r1.appendChild(auto);
    if (busy) r1.appendChild(el("span", "muted", "处理中…"));
    c1.appendChild(r1);
    c1.appendChild(el("div", "muted",
      "开机自启会创建一个名为 RMBCK 的计划任务（需要管理员权限创建，登录时以管理员权限启动，不弹 UAC）。"));
    p.appendChild(c1);

    // 修饰键屏蔽
    var c2 = el("div", "card");
    c2.appendChild(el("h3", null, "修饰键屏蔽"));
    c2.appendChild(el("div", "muted",
      "开启 = 屏蔽：按住该修饰键再长按右键不会触发联动；关闭 = 放行：对应组合键正常触发。"));
    var r2 = el("div", "row");
    r2.appendChild(settingSwitch("blockCtrl", s.blockCtrl, "Ctrl"));
    r2.appendChild(settingSwitch("blockShift", s.blockShift, "Shift"));
    r2.appendChild(settingSwitch("blockAlt", s.blockAlt, "Alt"));
    r2.appendChild(settingSwitch("blockWin", s.blockWin, "Win"));
    c2.appendChild(r2);
    p.appendChild(c2);

    // 弹窗与外观
    var c3 = el("div", "card");
    c3.appendChild(el("h3", null, "弹窗与外观"));
    var r3 = el("div", "row");
    r3.appendChild(settingSwitch("suppressToast", s.suppressToast, "进程切换提示"));
    r3.appendChild(settingSwitch("suppressEgg", s.suppressEgg, "彩蛋"));
    r3.appendChild(settingSwitch("suppressHud", s.suppressHud, "HUD 悬浮窗"));
    r3.appendChild(settingSwitch("suppressHotkeyToast", s.suppressHotkeyToast, "热键状态提示"));
    c3.appendChild(r3);
    var r3b = el("div", "row");
    r3b.appendChild(el("span", "muted", "主题"));
    var themeBtn = el("button", "btn", s.theme === "Night" ? "夜间（深色）" : "日间（樱花）");
    themeBtn.id = "themeToggleBtn";
    themeBtn.type = "button";
    themeBtn.onclick = function () {
      var next = s.theme === "Night" ? "Day" : "Night";
      applyTheme(next);           // 即时反馈；持久化由后端写指针文件
      if (state) state.theme = next;
      send("setTheme", { mode: next });
    };
    r3b.appendChild(themeBtn);
    c3.appendChild(r3b);
    p.appendChild(c3);

    // 配置与反馈
    var c4 = el("div", "card");
    c4.appendChild(el("h3", null, "配置与反馈"));
    var r4 = el("div", "row");
    r4.appendChild(el("span", "muted", "配置文件位置"));
    var folder = el("input");
    folder.type = "text";
    folder.id = "configFolderInput";
    folder.readOnly = true;
    folder.value = s.configFolder || "";
    folder.title = s.configFolder || "";
    folder.style.flex = "1";
    r4.appendChild(folder);
    var browse = el("button", "btn", "浏览…");
    browse.id = "browseFolderBtn";
    browse.type = "button";
    browse.onclick = function () { send("chooseFolder", { which: "config" }); };
    r4.appendChild(browse);
    c4.appendChild(r4);
    var r5 = el("div", "row");
    var fb = el("button", "btnGhost", "报错反馈");
    fb.id = "feedbackBtn";
    fb.type = "button";
    fb.onclick = function () { showFeedback(); };
    var home = el("button", "btnGhost", "作者主页");
    home.id = "homepageBtn";
    home.type = "button";
    home.onclick = function () { send("openHomepage", {}); };
    r5.appendChild(fb); r5.appendChild(home);
    c4.appendChild(r5);
    c4.appendChild(el("div", "muted footNote",
      "If there are any problems with this project, you don't have to go to the author."));
    c4.appendChild(el("div", "muted footNote",
      "使用本软件自动视为放弃自身一切权益，一切解释权归作者所有，点击访问作者个人主页"));
    p.appendChild(c4);
    restorePageState(p, pageState);
    window.__settingsRenderSig = sig;
  };

  function promptProfileName() {
    var w = $("modalWrap");
    w.innerHTML = "";
    var m = el("div", "modal");
    m.setAttribute("role", "dialog");
    m.setAttribute("aria-modal", "true");
    m.appendChild(el("h4", null, "新建配置方案"));
    var inp = el("input");
    inp.type = "text";
    inp.placeholder = "方案名称（最多 20 字）";
    inp.style.width = "100%";
    m.appendChild(inp);
    var acts = el("div", "actions");
    var bCancel = el("button", "btnGhost", "取消");
    var bOk = el("button", "btn", "创建");
    bCancel.onclick = function () { w.classList.remove("show"); };
    bOk.onclick = function () {
      w.classList.remove("show");
      send("newProfile", { name: inp.value });
    };
    acts.appendChild(bCancel);
    acts.appendChild(bOk);
    m.appendChild(acts);
    w.appendChild(m);
    w.classList.add("show");
    inp.focus();
  }
  window.__promptProfileName = promptProfileName;

  function confirmDeleteProfile(name) {
    var w = $("modalWrap");
    w.innerHTML = "";
    var m = el("div", "modal");
    m.setAttribute("role", "dialog");
    m.setAttribute("aria-modal", "true");
    m.appendChild(el("h4", null, "删除配置方案"));
    m.appendChild(el("div", "muted", "确定删除「" + name + "」？此操作不可恢复。"));
    var acts = el("div", "actions");
    var bCancel = el("button", "btnGhost", "取消");
    var bOk = el("button", "btnDanger", "删除");
    bCancel.onclick = function () { w.classList.remove("show"); };
    bOk.onclick = function () {
      w.classList.remove("show");
      send("deleteProfile", { name: name });
    };
    acts.appendChild(bCancel);
    acts.appendChild(bOk);
    m.appendChild(acts);
    w.appendChild(m);
    w.classList.add("show");
  }
  window.__confirmDeleteProfile = confirmDeleteProfile;

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
    var t = e.target;
    if (t && t.closest && t.closest(".winBtn")) return;
    if (e.button === 0) send("dragStart");
  });

  var modalWrap = $("modalWrap");
  modalWrap.addEventListener("click", function (e) {
    if (e.target === modalWrap) modalWrap.classList.remove("show");
  });
  document.addEventListener("keydown", function (e) {
    if (!modalWrap.classList.contains("show")) return;
    if (e.key === "Escape" || e.key === "Esc") {
      e.preventDefault();
      modalWrap.classList.remove("show");
    }
  });

  if (wv) {
    wv.addEventListener("message", function (e) {
      var msg = e.data;
      if (!msg || !msg.type) return;
      var data = msg.data;
      if (msg.type === "state") {
        state = data;
        window.__state = state;
        window.__autoBusy = !!(data && data.autostartBusy);
        // 后端进程查询标志（HWND 重建丢失回调时可在下一次 state 推送自愈）
        if (data && typeof data.processLoading === "boolean")
          window.__procLoading = data.processLoading;
        render();
      }
      else if (msg.type === "toast") { toast(data && data.text, data && data.kind, data && data.ms); }
      else if (msg.type === "progress") { showProgress(data || {}); }
      else if (msg.type === "askClose") { showCloseModal(); }
      else if (msg.type === "processes") {
        window.__processes = data || [];
        window.__procFetched = true;
        window.__procLoading = false;   // 枚举结束：恢复刷新按钮
        if (page === "process") render();
      }
      else if (msg.type === "history") {
        window.__history = data;
        if (page === "history") render();
      }
      else if (msg.type === "testStatus") {
        window.__testStatus = data;
        // 原地更新测试面板；不要 render()，否则 10Hz 刷新会销毁按钮/滑块并打断交互
        window.updateTestPanel(data);
      }
      else if (msg.type === "capturing") {
        window.__capturing = data;
        window.__captureRow = data && data.active && data.target === "row" ? data.index : null;
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
