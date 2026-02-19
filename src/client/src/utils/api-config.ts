
// Helper to determine the API base URL.
// In development, we point directly to the Kestrel backend (port 7101) to bypass the Vite proxy
// and ensure proper HTTP/2 support (browsers handle H2 better than Node proxies).
// In production, we use the relative path.

export const getApiBaseUrl = () => {
    if (import.meta.env.DEV) {
        return "https://localhost:7101";
    }
    return "";
};

export const getApiUrl = (path: string) => {
    const baseUrl = getApiBaseUrl();
    // Ensure path starts with /
    const normalizedPath = path.startsWith('/') ? path : `/${path}`;
    return `${baseUrl}${normalizedPath}`;
};
