/**
 * Frontend role name constants — must match backend AppRole enum
 * (src/SmartKb.Contracts/Enums/AppRole.cs).
 */
export const AppRoles = {
  Admin: 'Admin',
  SupportLead: 'SupportLead',
  SupportAgent: 'SupportAgent',
  EngineeringViewer: 'EngineeringViewer',
  SecurityAuditor: 'SecurityAuditor',
} as const;

export type AppRoleName = (typeof AppRoles)[keyof typeof AppRoles];
