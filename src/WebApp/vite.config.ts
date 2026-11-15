import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import { contentSecurityPolicy } from './build/contentSecurityPolicy.ts'

export default defineConfig({
  plugins: [react(), contentSecurityPolicy()],
  define: {
    // The About dialog's copyright year: the year of the build, not of the day the app is run.
    __BUILD_YEAR__: JSON.stringify(new Date().getFullYear()),
  },
  server: {
    port: 5223,
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    rollupOptions: {
      output: {
        entryFileNames: 'index.js',
        chunkFileNames: '[name]-[hash].js',
      },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    passWithNoTests: true,
  },
})
