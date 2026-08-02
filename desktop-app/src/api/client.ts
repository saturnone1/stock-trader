import axios from 'axios';

function resolveApiBaseUrl() {
  const envUrl = import.meta.env.VITE_API_URL;
  if (envUrl && (typeof window === 'undefined' || window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1')) {
    return envUrl;
  }

  if (typeof window !== 'undefined') {
    const { hostname } = window.location;

    if (hostname === 'localhost' || hostname === '127.0.0.1') {
      return 'http://localhost:5239';
    }

    return '';
  }

  return '';
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
