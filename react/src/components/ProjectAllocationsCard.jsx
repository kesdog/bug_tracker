import React from 'react';
import Avatar from '@mui/material/Avatar';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import Divider from '@mui/material/Divider';
import List from '@mui/material/List';
import ListItem from '@mui/material/ListItem';
import ListItemAvatar from '@mui/material/ListItemAvatar';
import ListItemText from '@mui/material/ListItemText';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { formatUserIdentity } from '../user_identity';

export default function ProjectAllocationsCard({
  associatedUsers,
  availableUsers,
  canManage,
  controlsDisabled,
  ownerUserId,
  saving,
  selectedUserId,
  visibility,
  onAllocate,
  onSelectedUserIdChange
}) {
  return (
    <Card variant="outlined">
      <CardContent>
        <Stack spacing={2}>
          <section aria-label="Explicit project allocations">
            <Typography component="h3" variant="h6">Explicit allocations</Typography>
            <Typography variant="body2" color="text.secondary">
              {visibility === 'normal'
                ? 'Listed users have explicit membership. Senior developers and admins may also have effective access through their roles.'
                : 'Sensitive-project access is limited to these explicitly allocated members.'}
            </Typography>
            {associatedUsers.length > 0 ? (
              <List disablePadding sx={{ mt: 1 }}>
                {associatedUsers.map((user, index) => (
                  <ListItem key={user.userId} divider={index < associatedUsers.length - 1} disableGutters>
                    <ListItemAvatar>
                      <Avatar sx={{ width: 34, height: 34, fontSize: '0.8rem' }}>
                        {user.userType === 'agent' ? 'AI' : String(user.userId).slice(0, 2).toUpperCase()}
                      </Avatar>
                    </ListItemAvatar>
                    <ListItemText
                      primary={(
                        <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                          <span>{formatUserIdentity(user)}</span>
                          {user.userId === ownerUserId ? <Chip size="small" label="Owner" color="primary" variant="outlined" /> : null}
                        </Stack>
                      )}
                      secondary={user.userType === 'agent' ? 'AI agent' : user.role}
                    />
                  </ListItem>
                ))}
              </List>
            ) : (
              <Typography color="text.secondary" sx={{ mt: 1 }}>No users are explicitly allocated to this project yet.</Typography>
            )}
          </section>

          {canManage ? (
            <>
              <Divider />
              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} sx={{ alignItems: { xs: 'stretch', sm: 'flex-start' } }}>
                <TextField
                  id="userSelect"
                  label="User"
                  value={selectedUserId}
                  onChange={(event) => onSelectedUserIdChange(event.target.value)}
                  disabled={controlsDisabled || availableUsers.length === 0}
                  helperText={visibility === 'sensitive' ? 'Membership is required before a user can receive sensitive-project tickets.' : 'Allocation grants project discovery and membership.'}
                  select
                  fullWidth
                  slotProps={{ select: { native: true } }}
                >
                  {availableUsers.map((user) => (
                    <option key={user.userId} value={user.userId}>{formatUserIdentity(user)}</option>
                  ))}
                </TextField>
                <Button
                  type="button"
                  onClick={onAllocate}
                  disabled={saving || availableUsers.length === 0}
                  sx={{ minWidth: { sm: 170 }, minHeight: 56 }}
                >
                  {saving ? 'Allocating...' : availableUsers.length === 0 ? 'No Users Available' : 'Allocate User'}
                </Button>
              </Stack>
            </>
          ) : (
            <Typography variant="body2" color="text.secondary">
              Sensitive-project allocations are read-only for senior developers. Only admins can change sensitive membership.
            </Typography>
          )}
        </Stack>
      </CardContent>
    </Card>
  );
}
