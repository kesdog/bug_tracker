export function formatUserIdentity(user) {
  if (!user) {
    return '';
  }

  const username = user.username || user.userId || 'Unknown user';
  return user.email ? `${username} - (${user.email})` : username;
}
