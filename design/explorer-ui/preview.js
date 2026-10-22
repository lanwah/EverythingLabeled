(function () {
  const params = new URLSearchParams(location.search);
  const panel = document.querySelector(".panel");
  const stateButtons = document.querySelectorAll(".state-toggle button");
  const modeSwitch = document.querySelector(".mode-switch-control");
  const modeLabel = document.querySelector("[data-mode-label]");
  const requestedState = params.get("state");
  const knownState = [...stateButtons].some((btn) => btn.dataset.state === requestedState);
  let state = knownState ? requestedState : "filled";

  const requestedView = params.get("view");
  const knownView = requestedView === "tag" || requestedView === "find";
  let view = knownView ? requestedView : "tag";

  function applyState(next) {
    state = next;
    if (panel) panel.dataset.state = state;
    stateButtons.forEach((btn) => {
      btn.setAttribute("aria-pressed", btn.dataset.state === state ? "true" : "false");
    });
    const url = new URL(location.href);
    url.searchParams.set("state", state);
    history.replaceState(null, "", url);
  }

  function applyView(next) {
    view = next;
    if (panel) panel.dataset.view = view;
    if (modeSwitch) {
      modeSwitch.setAttribute("aria-checked", view === "find" ? "true" : "false");
      modeSwitch.dataset.view = view;
    }
    if (modeLabel) {
      modeLabel.textContent = view === "find" ? "查找模式" : "打标模式";
    }
    document.querySelectorAll("[data-view-panel]").forEach((el) => {
      el.hidden = el.getAttribute("data-view-panel") !== view;
    });
    const url = new URL(location.href);
    url.searchParams.set("view", view);
    history.replaceState(null, "", url);
  }

  stateButtons.forEach((btn) => {
    btn.addEventListener("click", () => applyState(btn.dataset.state));
  });
  if (modeSwitch) {
    const toggleView = () => applyView(view === "tag" ? "find" : "tag");
    modeSwitch.addEventListener("click", toggleView);
    if (modeLabel) modeLabel.addEventListener("click", toggleView);
  }

  applyState(state);
  if (modeSwitch) applyView(view);
})();
