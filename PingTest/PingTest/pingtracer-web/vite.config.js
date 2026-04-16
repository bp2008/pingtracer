import { fileURLToPath, URL } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// https://vite.dev/config/
export default defineConfig({
	plugins: [
		vue(),
	],
	server: {
		allowedHosts: ['localhost'],
		proxy: {
			'/ws': {
				target: 'https://localhost:8010',
				ws: true,
				secure: false,
			},
		},
	},
	resolve: {
		alias: {
			'@': fileURLToPath(new URL('./src', import.meta.url))
		},
	},
})
