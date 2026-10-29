import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { DEFAULT_PREFS, htmlAttributes, loadPrefs, lookDefaults, parsePrefs, PREFS_KEY, savePrefs, type Prefs } from "./prefs";

const SCRIPT = readFileSync(path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../../public/prefs-init.js"), "utf8");

const runScript = () => {
  new Function(SCRIPT)();
  const d = document.documentElement;
  return Object.fromEntries(
    ["data-skin", "data-texture", "data-solid", "data-theme", "data-quiet", "data-text-size", "data-spacing", "data-density", "data-reduce-motion"].map((k) => [k, d.getAttribute(k)]),
  );
};

const setSystemDark = (dark: boolean) => {
  window.matchMedia = ((q: string) => ({ matches: dark && q.includes("dark"), media: q, addEventListener() {}, removeEventListener() {} })) as unknown as typeof window.matchMedia;
};

afterEach(() => {
  window.localStorage.clear();
  document.documentElement.removeAttribute("data-themes");
});

describe("prefs parsing", () => {
  it("defaults when nothing or garbage is stored", () => {
    expect(parsePrefs(null)).toEqual(DEFAULT_PREFS);
    expect(parsePrefs({ theme: "neon", textSize: 99, quiet: "yes", density: ["x"] })).toEqual(DEFAULT_PREFS);
  });

  it("themes (BL-001): skin, texture and solid are additive fields; unknown skins fall back to Paper; never inferred", () => {
    expect(DEFAULT_PREFS).toMatchObject({ skin: "paper", texture: false, solid: false });
    expect(parsePrefs({ skin: "glass", texture: true, solid: true })).toMatchObject({ skin: "glass", texture: true, solid: true });
    expect(parsePrefs({ skin: "canvas" }).skin).toBe("paper");
    expect(parsePrefs({ skin: "GAMER", texture: "true", solid: 1 })).toMatchObject({ skin: "paper", texture: false, solid: false });
    // An older stored object (no skin field) keeps Paper and every other choice.
    expect(parsePrefs({ theme: "dark", quiet: true })).toMatchObject({ skin: "paper", theme: "dark", quiet: true });
  });

  it("Reset look restores only look fields (lecture settings and Focus mode are untouched)", () => {
    const d = lookDefaults();
    expect(d).toEqual({ skin: "paper", theme: "system", quiet: false, texture: false, solid: false, textSize: "m", spacing: "standard", density: "comfortable", reduceMotion: false });
    expect(Object.keys(d)).not.toContain("focusMode");
    expect(Object.keys(d)).not.toContain("showSeconds");
  });

  it("migrates Reduce motion from the older lecture settings key and never deletes that key", () => {
    window.localStorage.setItem("tether.lecture.settings.v1", JSON.stringify({ reduceMotion: true, announceLines: true }));
    expect(loadPrefs().reduceMotion).toBe(true);
    savePrefs({ ...loadPrefs(), reduceMotion: false });
    expect(loadPrefs().reduceMotion).toBe(false); // the new key wins once it exists
    expect(window.localStorage.getItem("tether.lecture.settings.v1")).toContain("announceLines");
  });

  it("survives blocked storage", () => {
    const get = vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new Error("blocked");
    });
    expect(loadPrefs()).toEqual(DEFAULT_PREFS);
    get.mockRestore();
  });
});

describe("pre-hydration script (no dark flash) agrees with htmlAttributes", () => {
  const cases: [Partial<Prefs> | null, boolean][] = [
    [null, false],
    [null, true],
    [{ theme: "dark" }, false],
    [{ theme: "light" }, true],
    [{ theme: "system", quiet: true, textSize: "l", spacing: "wide", density: "compact", reduceMotion: true }, true],
    [{ theme: "dark", quiet: true, textSize: "s" }, false],
    [{ skin: "portal", theme: "dark" }, false],
    [{ skin: "gamer", texture: true }, true],
    [{ skin: "glass", solid: true, quiet: true }, false],
    [{ skin: "calm", theme: "light", density: "compact" }, true],
    [{ skin: "nonsense", texture: "yes" } as unknown as Partial<Prefs>, false],
  ];
  it.each(cases)("stored %j, system dark %s", (stored, systemDark) => {
    setSystemDark(systemDark);
    if (stored) window.localStorage.setItem(PREFS_KEY, JSON.stringify(stored));
    expect(runScript()).toEqual(htmlAttributes(parsePrefs(stored), systemDark));
  });

  it("rollback flag: <html data-themes=off> forces Paper and keeps the stored skin", () => {
    setSystemDark(false);
    window.localStorage.setItem(PREFS_KEY, JSON.stringify({ skin: "gamer", theme: "dark" }));
    document.documentElement.setAttribute("data-themes", "off");
    const got = runScript();
    expect(got["data-skin"]).toBe("paper");
    expect(got["data-theme"]).toBe("dark");
    expect(got).toEqual(htmlAttributes(parsePrefs({ skin: "gamer", theme: "dark" }), false, false));
    expect(JSON.parse(window.localStorage.getItem(PREFS_KEY) ?? "{}").skin).toBe("gamer");
  });

  it("does not throw on corrupt JSON and still resolves the system theme", () => {
    setSystemDark(true);
    window.localStorage.setItem(PREFS_KEY, "{not json");
    expect(runScript()["data-theme"]).toBe("dark");
  });
});
