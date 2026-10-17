(function () {
  const MOCK_SUGGEST = ["工作", "工作区", "工作笔记"];
  const PATH_COUNT = 3;

  const panel = document.querySelector(".panel");
  const aliasInput = document.getElementById("alias-input");
  const suggestList = document.getElementById("alias-suggest");
  const chipWrap = document.getElementById("tag-chips");
  const statusEl = document.getElementById("tag-status");
  const createMissing = document.getElementById("create-missing");
  const submitBtn = document.getElementById("tag-submit");

  if (!panel || !aliasInput || !suggestList || !chipWrap || !statusEl) return;

  const applied = [];
  let suggestItems = [];
  let suggestIndex = -1;
  /** Non-null while footer should keep the last batch summary until the user edits the alias field. */
  let batchStatusUntilEdit = null;
  let hadAliasDraft = false;

  function normalize(text) {
    const t = (text ?? "").trim();
    return t.length ? t : null;
  }

  function panelState() {
    return panel.dataset.state === "empty" ? "empty" : "filled";
  }

  function pathCount() {
    return panelState() === "empty" ? 0 : PATH_COUNT;
  }

  function setCreateMissingFromQuery(q) {
    if (!createMissing || panelState() === "empty") return;
    const exact = MOCK_SUGGEST.some((a) => a.toLowerCase() === q.toLowerCase());
    createMissing.checked = !exact;
  }

  function renderChips() {
    chipWrap.innerHTML = "";
    applied.forEach((alias) => {
      const chip = document.createElement("span");
      chip.className = "chip";
      chip.innerHTML = `${escapeHtml(alias)} <button type="button" aria-label="去标 ${escapeHtml(alias)}">×</button>`;
      chip.querySelector("button").addEventListener("click", () => removeTag(alias));
      chipWrap.appendChild(chip);
    });
    aliasInput.placeholder = applied.length ? "继续输入…" : "输入别名…";
  }

  function escapeHtml(s) {
    return s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
  }

  function renderSuggest() {
    suggestList.innerHTML = "";
    const q = normalize(aliasInput.value);
    if (!q || panelState() === "empty") {
      suggestItems = [];
      suggestIndex = -1;
      suggestList.hidden = true;
      return;
    }

    suggestItems = MOCK_SUGGEST.filter((a) => a.includes(q));
    suggestIndex = suggestItems.length > 0 ? 0 : -1;

    suggestItems.forEach((item, i) => {
      const li = document.createElement("li");
      li.textContent = item;
      if (i === suggestIndex) li.classList.add("selected");
      li.addEventListener("mousedown", (e) => {
        e.preventDefault();
        pickSuggest(i);
      });
      suggestList.appendChild(li);
    });

    suggestList.hidden = suggestItems.length === 0;
    setCreateMissingFromQuery(q);
  }

  function highlightSuggest() {
    suggestList.querySelectorAll("li").forEach((li, i) => {
      li.classList.toggle("selected", i === suggestIndex);
    });
  }

  function pickSuggest(index) {
    if (index < 0 || index >= suggestItems.length) return;
    aliasInput.value = suggestItems[index];
    suggestList.hidden = true;
    suggestItems = [];
    suggestIndex = -1;
    updateStatus();
    setCreateMissingFromQuery(normalize(aliasInput.value) ?? "");
  }

  function updateStatus() {
    if (panelState() === "empty") {
      statusEl.textContent = "准备就绪";
      return;
    }

    const alias = normalize(aliasInput.value);
    const n = pathCount();
    if (!alias) {
      statusEl.textContent = n > 0 ? "准备就绪" : "没有可提交的路径";
      return;
    }
    if (n === 0) {
      statusEl.textContent = "没有可提交的路径";
      return;
    }
    statusEl.textContent = `将为 ${n} 项打上「${alias}」`;
  }

  function refreshStatusFromInput() {
    const alias = normalize(aliasInput.value);
    if (batchStatusUntilEdit) {
      if (!alias) {
        if (hadAliasDraft) batchStatusUntilEdit = null;
        else {
          statusEl.textContent = batchStatusUntilEdit;
          hadAliasDraft = false;
          return;
        }
      } else {
        batchStatusUntilEdit = null;
      }
    }
    hadAliasDraft = alias !== null;
    updateStatus();
  }

  function resolveAliasForSubmit() {
    const draft = normalize(aliasInput.value);
    if (!draft) return null;
    if (!suggestList.hidden && suggestIndex >= 0 && suggestItems[suggestIndex]) {
      return suggestItems[suggestIndex];
    }
    return draft;
  }

  function submitTag() {
    if (panelState() === "empty") return;
    const alias = resolveAliasForSubmit();
    if (!alias) {
      statusEl.textContent = "请输入一个别名";
      return;
    }
    const n = pathCount();
    if (n === 0) {
      statusEl.textContent = "没有可提交的路径";
      return;
    }

    if (!applied.includes(alias)) applied.push(alias);
    aliasInput.value = "";
    suggestList.hidden = true;
    suggestItems = [];
    suggestIndex = -1;
    renderChips();
    batchStatusUntilEdit = `完成：成功 ${n}，失败 0，已取消未发送 0`;
    statusEl.textContent = batchStatusUntilEdit;
    hadAliasDraft = false;
    aliasInput.focus();
  }

  function removeTag(alias) {
    const idx = applied.indexOf(alias);
    if (idx < 0) return;
    applied.splice(idx, 1);
    renderChips();
    refreshStatusFromInput();
  }

  function moveSuggest(delta) {
    if (suggestList.hidden || suggestItems.length === 0) return false;
    if (suggestIndex < 0) suggestIndex = 0;
    else suggestIndex = Math.max(0, Math.min(suggestItems.length - 1, suggestIndex + delta));
    highlightSuggest();
    return true;
  }

  aliasInput.addEventListener("input", () => {
    renderSuggest();
    refreshStatusFromInput();
  });

  aliasInput.addEventListener("keydown", (e) => {
    if (e.key === "ArrowDown") {
      if (moveSuggest(1)) e.preventDefault();
      return;
    }
    if (e.key === "ArrowUp") {
      if (moveSuggest(-1)) e.preventDefault();
      return;
    }
    if (e.key === "Enter") {
      e.preventDefault();
      if (!suggestList.hidden && suggestItems.length > 0 && suggestIndex >= 0) {
        aliasInput.value = suggestItems[suggestIndex];
        suggestList.hidden = true;
        suggestItems = [];
        suggestIndex = -1;
        setCreateMissingFromQuery(normalize(aliasInput.value) ?? "");
      }
      submitTag();
    }
    if (e.key === "Escape") {
      suggestList.hidden = true;
      suggestItems = [];
      suggestIndex = -1;
    }
  });

  submitBtn?.addEventListener("click", () => submitTag());

  document.querySelector(".state-toggle")?.addEventListener("click", () => {
    setTimeout(() => {
      const empty = panelState() === "empty";
      aliasInput.readOnly = empty;
      aliasInput.disabled = empty;
      if (empty) {
        suggestList.hidden = true;
      } else {
        renderSuggest();
      }
      batchStatusUntilEdit = null;
      refreshStatusFromInput();
    }, 0);
  });

  renderChips();
  refreshStatusFromInput();
})();

