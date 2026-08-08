(() => {
  "use strict";

  const meetingCodePattern = /^\/[a-z]+-[a-z]+-[a-z]+\/?$/i;

  function publish(state = "open") {
    const path = window.location.pathname;
    if (!meetingCodePattern.test(path)) {
      return;
    }

    const meetingCode = path.replaceAll("/", "").toLowerCase();
    const browser = navigator.userAgent.includes("Edg/") ? "msedge" : "chrome";
    chrome.runtime.sendMessage({ type: "meet-page", state, meetingCode, browser });
  }

  publish();
  window.addEventListener("pageshow", () => publish());
  window.addEventListener("pagehide", () => publish("closed"));
  document.addEventListener("visibilitychange", () => publish());
  setInterval(() => publish(), 10000);
})();
