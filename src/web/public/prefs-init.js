// Loaded synchronously in <head> before the body paints, so a student who chose dark never sees a light flash.
// A static same-origin file rather than inline script (the source tree bans dangerouslySetInnerHTML).
// Mirrors parsePrefs/htmlAttributes in src/lib/design/prefs.ts; prefs.test.ts runs this file and checks they agree.
(function () {
  try {
    var d = document.documentElement, p = {}, l = {};
    try { p = JSON.parse(localStorage.getItem("tether.prefs.v1") || "{}") || {}; } catch { p = {}; }
    try { l = JSON.parse(localStorage.getItem("tether.lecture.settings.v1") || "{}") || {}; } catch { l = {}; }
    var dark = !!(window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches);
    var t = p.theme === "light" || p.theme === "dark" ? p.theme : dark ? "dark" : "light";
    d.setAttribute("data-theme", t);
    d.setAttribute("data-quiet", p.quiet === true ? "true" : "false");
    d.setAttribute("data-text-size", p.textSize === "s" || p.textSize === "l" ? p.textSize : "m");
    d.setAttribute("data-spacing", p.spacing === "wide" ? "wide" : "standard");
    d.setAttribute("data-density", p.density === "compact" ? "compact" : "comfortable");
    var rm = typeof p.reduceMotion === "boolean" ? p.reduceMotion : l.reduceMotion === true;
    d.setAttribute("data-reduce-motion", rm ? "true" : "false");
  } catch {
    /* storage blocked: CSS defaults (Paper) apply */
  }
})();
