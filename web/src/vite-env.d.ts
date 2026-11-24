/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Full SignalR endpoint, e.g. https://host/hub. Defaults to /hub (dev proxy). */
  readonly VITE_HUB_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
