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

  // 通用组件
  window.__switch = function (on, onChange) {
    var b = el("button", "sw " + (on ? "on" : ""));
    b.appendChild(el("span"));
    b.onclick = function () { onChange(!on); };
    return b;
  };

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
    delay.disabled = k.mode === "Hold";
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

  window.render_keys = function (s) {
    var p = document.getElementById("page-keys");
    if (!p) return;
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
  };

  // ---------- 概览页 ----------
  window.render_overview = function (s) {
    var p = document.getElementById("page-overview");
    if (!p) return;
    p.innerHTML = "";

    // 总开关 + 方案
    var c1 = el("div", "card");
    c1.appendChild(el("h3", null, "联动"));
    var row1 = el("div", "row");
    row1.appendChild(window.__switch(s.enabled, function (v) { send("toggleEnabled", { enabled: v }); }));
    row1.appendChild(el("span", "muted", s.enabled ? "当前联动状态：启用" : "当前联动状态：禁用"));
    row1.appendChild(el("span", "spacer"));
    var sel = el("select");
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
    bNew.onclick = function () { promptProfileName(); };
    var bDel = el("button", "btnGhost", "－ 删除");
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
    rng.min = "100";
    rng.max = "2000";
    rng.step = "10";
    rng.value = s.longPressMs;
    rng.style.width = "260px";
    var num = el("input");
    num.type = "number";
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
    row2.appendChild(window.__switch(s.randomJitter, function (v) { send("setSetting", { key: "randomJitter", value: v }); }));
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
