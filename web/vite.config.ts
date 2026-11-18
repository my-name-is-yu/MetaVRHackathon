import { iwsdkDev } from '@iwsdk/vite-plugin-dev';
import { defineConfig } from 'vite';

export default defineConfig({
  plugins: [iwsdkDev()],
  server: { host: '0.0.0.0' },
  base: './',
  build: { target: 'esnext' },
  optimizeDeps: { esbuildOptions: { target: 'esnext' } }
});
