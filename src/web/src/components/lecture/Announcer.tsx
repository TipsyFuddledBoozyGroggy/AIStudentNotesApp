"use client";

import { createContext, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { AnnouncementQueue, type Emitted } from "./announcerQueue";

export interface Announcer {
  /** role=status, polite. */
  status(text: string): void;
  /** role=alert; once per episode `key`. */
  alert(key: string, text: string): void;
  clearEpisode(key: string): void;
  /** Text for the (opt-in) finals region inside the transcript log. */
  finalsText: string;
  setFinalsText(text: string): void;
}

const Ctx = createContext<Announcer | null>(null);

export function useAnnouncer(): Announcer {
  const v = useContext(Ctx);
  if (!v) throw new Error("useAnnouncer must be used inside AnnouncerProvider");
  return v;
}

const EMPTY: Emitted = { id: 0, text: "" };

/**
 * Mounts the status and alert regions exactly once and keeps them for the life of the provider.
 * (The third region, finals, lives inside the transcript log and is fed through finalsText.)
 * No other element in the lecture screens may use aria-live, role=status or role=alert.
 */
export function AnnouncerProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<Emitted>(EMPTY);
  const [alertMsg, setAlertMsg] = useState<Emitted>(EMPTY);
  const [finalsText, setFinalsText] = useState("");
  const q = useRef<AnnouncementQueue | null>(null);
  if (q.current === null) {
    q.current = new AnnouncementQueue({
      now: () => Date.now(),
      setTimeout: (fn, ms) => setTimeout(fn, ms),
      clearTimeout: (h) => clearTimeout(h as ReturnType<typeof setTimeout>),
      onStatus: setStatus,
      onAlert: setAlertMsg,
    });
  }
  useEffect(() => {
    const queue = q.current;
    return () => queue?.dispose();
  }, []);

  const value = useMemo<Announcer>(
    () => ({
      status: (t) => q.current?.status(t),
      alert: (k, t) => q.current?.alert(k, t),
      clearEpisode: (k) => q.current?.clearEpisode(k),
      finalsText,
      setFinalsText,
    }),
    [finalsText],
  );

  return (
    <Ctx.Provider value={value}>
      {/* Replacing the inner span (new key) is a content mutation, so identical later text is re-announced. */}
      <div role="status" data-region="status" className="sr-only">
        <span key={status.id}>{status.text}</span>
      </div>
      <div role="alert" data-region="alert" className="sr-only">
        <span key={alertMsg.id}>{alertMsg.text}</span>
      </div>
      {children}
    </Ctx.Provider>
  );
}
