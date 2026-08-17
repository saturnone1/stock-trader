import axios from 'axios';

function resolveApiBaseUrl() {
  // Production ingress, the local container, and the Vite proxy all expose
  // /api on the UI origin. An explicit build-time URL remains available for
  // unusual standalone development setups.
  return String(import.meta.env.VITE_API_URL ?? '').trim().replace(/\/+$/, '');
}

const API_BASE_URL = resolveApiBaseUrl();

export const api = axios.create({
  baseURL: API_BASE_URL,
  timeout: 30000,
  withCredentials: true,
});

api.interceptors.response.use(
  response => response,
  error => {
    console.error('[API Error]', error.response?.status, error.message);
    return Promise.reject(error);
  }
);
