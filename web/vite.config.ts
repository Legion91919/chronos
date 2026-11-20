/// <reference types="vitest" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Dev proxy: the browser talks to Vite; /api and /health go to the Chronos API on :5204.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5174,
    proxy: {
      '/api': 'http://localhost:5204',
      '/health': 'http://localhost:5204',
    },
  },
  test: { environment: 'node' },
});
