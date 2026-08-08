"use strict";

const nativeHostName = "com.meetingorganizer.gemini";
const activeTabs = new Map();
let nativePort = null;

function connectNativeHost() {
  if (nativePort) {
    return nativePort;
  }

  nativePort = chrome.runtime.connectNative(nativeHostName);
  nativePort.onDisconnect.addListener(() => {
    nativePort = null;
  });
  return nativePort;
}

function publishState() {
  const distinct = new Map();
  for (const value of activeTabs.values()) {
    distinct.set(value.meetingCode, value);
  }

  try {
    connectNativeHost().postMessage({ links: Array.from(distinct.values()) });
  } catch {
    nativePort = null;
  }
}

chrome.runtime.onMessage.addListener((message, sender) => {
  const tabId = sender.tab?.id;
  if (tabId === undefined || message?.type !== "meet-page") {
    return;
  }

  if (message.state === "closed") {
    activeTabs.delete(tabId);
  } else {
    activeTabs.set(tabId, {
      meetingCode: message.meetingCode,
      browser: message.browser
    });
  }
  publishState();
});

chrome.tabs.onRemoved.addListener((tabId) => {
  if (activeTabs.delete(tabId)) {
    publishState();
  }
});
