// Shapes from the auth contract (auth-contract.md). JSON is camelCase.

export type Role = 'User' | 'Admin';

export interface UserDto {
  id: string;
  email: string;
  displayName: string;
  timeZone: string;
  roles: string[];
  emailConfirmed: boolean;
  createdAt: string;
}

export interface AdminUserDto {
  id: string;
  email: string;
  displayName: string;
  roles: string[];
  emailConfirmed: boolean;
  lockedOut: boolean;
  createdAt: string;
  lastLoginAt: string | null;
}

export interface AdminUserPage {
  items: AdminUserDto[];
  total: number;
}

export interface RegisterRequest {
  email: string;
  password: string;
  displayName: string;
}

export interface LoginRequest {
  email: string;
  password: string;
  rememberMe: boolean;
}

export interface ResetPasswordRequest {
  email: string;
  token: string;
  newPassword: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

export interface UpdateProfileRequest {
  displayName: string;
  timeZone: string;
}

/** Error `code` extension values the API sends on ProblemDetails responses. */
export type AuthErrorCode =
  | 'antiforgery'
  | 'invalid_credentials'
  | 'email_not_confirmed'
  | 'locked_out'
  | 'invalid_token'
  | 'validation'
  | 'unauthenticated'
  | 'forbidden'
  | 'rate_limited'
  | 'cannot_lock_self'
  | 'cannot_delete_self'
  | 'last_admin';

export const PASSWORD_MIN_LENGTH = 10;
export const PASSWORD_MAX_LENGTH = 128;
export const DISPLAY_NAME_MAX_LENGTH = 100;
