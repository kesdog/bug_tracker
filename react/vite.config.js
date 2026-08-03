import { defineConfig, loadEnv } from 'vite';
import { resolve } from 'node:path';

export default defineConfig(({ mode }) => {
  const envDir = resolve(__dirname, '..');
  const env = loadEnv(mode, envDir, '');
  const apiProxyTarget = env.VITE_API_PROXY_TARGET?.trim() || 'http://127.0.0.1:5040';

  return {
    envDir,
    server: {
      host: '127.0.0.1',
      port: 5173,
      proxy: {
        '/api': {
          target: apiProxyTarget,
          changeOrigin: true
        }
      },
      fs: {
        allow: ['..']
      }
    },
    test: {
      environment: 'jsdom',
      globals: true,
      // Avoid oversubscribing CPU-heavy jsdom/user-event suites.
      maxWorkers: 2,
      testTimeout: 15000,
      setupFiles: './vitest.setup.js',
      include: ['../testing/frontend/**/*.test.{js,jsx}'],
      deps: {
        moduleDirectories: [
          'node_modules',
          resolve(__dirname, 'node_modules')
        ]
      }
    },
    build: {
      rollupOptions: {
        output: {
          manualChunks(id) {
            if (id.includes('/node_modules/@mui/x-data-grid/')) {
              return 'mui-data-grid';
            }

            if (id.includes('/node_modules/@mui/') || id.includes('/node_modules/@emotion/')) {
              return 'mui';
            }

            if (id.includes('/node_modules/react/') || id.includes('/node_modules/react-dom/')) {
              return 'react';
            }
          }
        },
        onwarn(warning, warn) {
          // MUI v9 publishes React Server Component directives that Vite's browser bundle intentionally ignores.
          if (warning.code === 'MODULE_LEVEL_DIRECTIVE' && warning.message.includes('use client')) {
            return;
          }

          warn(warning);
        }
      }
    }
  };
});
