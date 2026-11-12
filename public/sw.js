/* Annaba Digital service worker — deliberately minimal.
 * SECURITY POLICY: this is an identity/access app. We NEVER cache HTML documents, RSC payloads, API
 * responses or anything personalised. Only (a) public, content-hashed build assets and (b) one static
 * offline page are cached. Everything else goes straight to the network. */
const VERSION = "v1";
const STATIC_CACHE = `annaba-digital-static-${VERSION}`;
const OFFLINE_URL = "/offline";

self.addEventListener("install", (event) => {
  event.waitUntil(caches.open(STATIC_CACHE).then((c) => c.add(OFFLINE_URL)).then(() => self.skipWaiting()));
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches.keys().then((keys) => Promise.all(keys.filter((k) => k !== STATIC_CACHE).map((k) => caches.delete(k)))).then(() => self.clients.claim()),
  );
});

// Lets the app wipe everything (e.g. on sign-out) — defence in depth, the cache holds no user data.
self.addEventListener("message", (event) => {
  if (event.data === "CLEAR_CACHES") event.waitUntil(caches.keys().then((keys) => Promise.all(keys.map((k) => caches.delete(k)))));
});

function isCacheableAsset(url) {
  return url.pathname.startsWith("/_next/static/") || url.pathname.startsWith("/icons/");
}

self.addEventListener("fetch", (event) => {
  const req = event.request;
  if (req.method !== "GET") return;
  const url = new URL(req.url);
  if (url.origin !== self.location.origin) return;

  if (isCacheableAsset(url) && !url.search) {
    event.respondWith(
      caches.open(STATIC_CACHE).then(async (cache) => {
        const hit = await cache.match(req);
        if (hit) return hit;
        const res = await fetch(req);
        if (res.ok) cache.put(req, res.clone());
        return res;
      }),
    );
    return;
  }

  // Page navigations: network only; when offline show the static fallback. Responses are never stored.
  if (req.mode === "navigate") {
    event.respondWith(fetch(req).catch(async () => (await caches.match(OFFLINE_URL)) || Response.error()));
  }
  // Anything else (API, RSC, data): not intercepted.
});

// Push notifications (Web Push). The message holds what the bell shows: a title, a short text and a link inside the app. Nothing is cached.
self.addEventListener("push", (event) => {
  let data = {};
  try { data = event.data ? event.data.json() : {}; } catch { data = { title: event.data ? event.data.text() : "Annaba HRIS" }; }
  const title = data.title || "Annaba HRIS";
  event.waitUntil(self.registration.showNotification(title, { body: data.body || "", icon: "/icons/192", badge: "/icons/192", data: { link: data.link || "/" }, tag: data.link || undefined }));
});

// Opening a notification focuses an open tab of the app (and moves it to the link) or opens a new one. Only links inside the app are followed.
self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  const raw = (event.notification.data && event.notification.data.link) || "/";
  const target = new URL(raw.startsWith("/") ? raw : "/", self.location.origin).href;
  event.waitUntil(self.clients.matchAll({ type: "window", includeUncontrolled: true }).then((list) => {
    for (const c of list) if (new URL(c.url).origin === self.location.origin && "focus" in c) { c.navigate(target); return c.focus(); }
    return self.clients.openWindow(target);
  }));
});
