// vitest/config, not vite: the `test` block below is not part of Vite's own config type.
import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import { powerApps } from "@microsoft/power-apps-vite/plugin"

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), powerApps()],
  test: {
    // Vitest stubs every CSS import to an empty string by default, including one asking
    // for the file's text with ?raw. appShellResponsive.test.ts reads AppShell.css that
    // way to check a layout rule that has no DOM here to lay out (F19).
    css: true,
  },
});
