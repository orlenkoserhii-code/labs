"use strict";

// Each page obtains a fresh token for its current cookie identity before a mutation.
// No token is persisted or logged, and a failed mutation is never replayed automatically.
window.secureLabFetch = async function (path, options = {}) {
  const url = new URL(path, location.origin);
  if (url.origin !== location.origin) {
    throw new Error("Потрібна адреса цього застосунку.");
  }

  const headers = new Headers(options.headers);
  const method = (options.method ?? "GET").toUpperCase();
  const credentialEndpoint = ["/api/auth/login", "/api/auth/register"].includes(url.pathname);
  if (["POST", "PUT", "PATCH", "DELETE"].includes(method) && !credentialEndpoint) {
    const tokenResponse = await fetch("/api/auth/csrf-token", {
      credentials: "same-origin",
      cache: "no-store",
    });
    if (!tokenResponse.ok) return tokenResponse;
    const { token } = await tokenResponse.json();
    headers.set("X-CSRF-TOKEN", token);
  }

  return fetch(url, {
    ...options,
    headers,
    credentials: "same-origin",
    cache: "no-store",
  });
};
