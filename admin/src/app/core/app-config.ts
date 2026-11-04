export const API_BASE_URL = '';

export const ACCESS_TOKEN_KEY = 'eh.access_token';
export const REFRESH_TOKEN_KEY = 'eh.refresh_token';
export const ACCESS_EXPIRY_KEY = 'eh.access_expires';
export const REFRESH_EXPIRY_KEY = 'eh.refresh_expires';
export const USER_KEY = 'eh.user';
export const LANGUAGE_KEY = 'eh.language';

export const SUPPORTED_LANGUAGES: ReadonlyArray<{ code: string; label: string }> = [
  { code: 'ar', label: 'العربية' },
  { code: 'en', label: 'English' }
];
