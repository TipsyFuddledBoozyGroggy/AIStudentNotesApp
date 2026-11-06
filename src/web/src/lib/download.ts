/**
 * Saves text as a file through the browser's download, from a student's own press. Never rendered into the page, never
 * sent anywhere: the Blob URL is local and revoked shortly after the click. Returns false when this browser can not
 * (no Blob URLs, or the click threw), so the caller can say so instead of claiming a file exists.
 * Shared by the syllabus JSON export and the lecture notes export (rule-4 sweep, finish-backlog item 2).
 */
export function downloadTextFile(text: string, name: string, type: string): boolean {
  try {
    if (typeof URL.createObjectURL !== "function") return false;
    const url = URL.createObjectURL(new Blob([text], { type }));
    const a = document.createElement("a");
    a.href = url;
    a.download = name;
    a.rel = "noopener";
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
    return true;
  } catch {
    return false;
  }
}

/** Copies text to the clipboard from a student's own press. Resolves false when the browser refuses or has no clipboard. */
export async function copyText(text: string): Promise<boolean> {
  try {
    if (typeof navigator === "undefined" || !navigator.clipboard?.writeText) return false;
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    return false;
  }
}
