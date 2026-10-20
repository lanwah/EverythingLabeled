(function () {
  const params = new URLSearchParams(location.search);
  const panel = document.querySelector(".panel");
  const buttons = document.querySelectorAll(".state-toggle button");
  const requested = params.get("state");
  const known = [...buttons].some((btn) => btn.dataset.state === requested);
  let state = known ? requested : "filled";

  function apply(next) {
    state = next;
    if (panel) panel.dataset.state = state;
    buttons.forEach((btn) => {
      btn.setAttribute("aria-pressed", btn.dataset.state === state ? "true" : "false");
    });
    const url = new URL(location.href);
    url.searchParams.set("state", state);
    history.replaceState(null, "", url);
  }

  buttons.forEach((btn) => {
    btn.addEventListener("click", () => apply(btn.dataset.state));
  });

  apply(state);
})();
