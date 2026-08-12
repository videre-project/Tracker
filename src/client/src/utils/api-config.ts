/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


// Browser requests are relative by default so development uses Vite's proxy and
// production uses Tracker's same-origin API. A host may explicitly override it.

export const getApiBaseUrl = () => {
    const configured = import.meta.env.VITE_API_BASE_URL?.trim();
    return configured ? configured.replace(/\/$/, "") : "";
};

export const getApiUrl = (path: string) => {
    const baseUrl = getApiBaseUrl();
    // Ensure path starts with /
    const normalizedPath = path.startsWith('/') ? path : `/${path}`;
    return `${baseUrl}${normalizedPath}`;
};
