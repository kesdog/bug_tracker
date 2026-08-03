import React, { useEffect, useState } from 'react';
import AppBar from '@mui/material/AppBar';
import Avatar from '@mui/material/Avatar';
import Badge from '@mui/material/Badge';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Divider from '@mui/material/Divider';
import Drawer from '@mui/material/Drawer';
import IconButton from '@mui/material/IconButton';
import List from '@mui/material/List';
import ListItemButton from '@mui/material/ListItemButton';
import ListItemIcon from '@mui/material/ListItemIcon';
import ListItemText from '@mui/material/ListItemText';
import Menu from '@mui/material/Menu';
import MenuItem from '@mui/material/MenuItem';
import Stack from '@mui/material/Stack';
import Toolbar from '@mui/material/Toolbar';
import Typography from '@mui/material/Typography';
import useMediaQuery from '@mui/material/useMediaQuery';
import { useTheme } from '@mui/material/styles';
import AddCircleOutlinedIcon from '@mui/icons-material/AddCircleOutlined';
import AssignmentTurnedInIcon from '@mui/icons-material/AssignmentTurnedIn';
import BugReportIcon from '@mui/icons-material/BugReport';
import DashboardIcon from '@mui/icons-material/Dashboard';
import FolderCopyIcon from '@mui/icons-material/FolderCopy';
import HistoryIcon from '@mui/icons-material/History';
import LogoutIcon from '@mui/icons-material/Logout';
import MenuIcon from '@mui/icons-material/Menu';
import NotificationsIcon from '@mui/icons-material/Notifications';
import PeopleAltIcon from '@mui/icons-material/PeopleAlt';
import ReceiptLongIcon from '@mui/icons-material/ReceiptLong';
import SendIcon from '@mui/icons-material/Send';
import { fetchNotifications, markAllNotificationsRead, markNotificationRead } from '../api/notifications';
import ColorModeToggle from './ColorModeToggle';

export const drawerWidth = 268;

export default function NavBar({ currentPage, onNavigate, userRole, token, user, onLogout }) {
  const [mobileOpen, setMobileOpen] = useState(false);
  const [notificationAnchor, setNotificationAnchor] = useState(null);
  const [notifications, setNotifications] = useState([]);
  const [notificationsAvailable, setNotificationsAvailable] = useState(false);
  const [notificationError, setNotificationError] = useState('');
  const theme = useTheme();
  const desktop = useMediaQuery(theme.breakpoints.up('md'));
  const isHuman = user?.userType !== 'agent';
  const canManageProjects = isHuman && (userRole === 'senior' || userRole === 'admin');
  const canManageUsers = isHuman && userRole === 'admin';
  const canTrackSubmitted = user?.userType !== 'agent' && (userRole === 'dev' || userRole === 'senior');
  const notificationsOpen = Boolean(notificationAnchor);

  const navigationItems = [
    { page: 'dashboard', label: 'Dashboard', icon: <DashboardIcon /> },
    { page: 'tickets', label: 'View Tickets', icon: <BugReportIcon /> },
    { page: 'allocated', label: 'Allocated Bugs', icon: <AssignmentTurnedInIcon /> },
    ...(canTrackSubmitted ? [{ page: 'submitted', label: 'Submitted', icon: <SendIcon /> }] : []),
    { page: 'archived', label: 'Archived', icon: <HistoryIcon /> },
    { page: 'add-bug', label: 'Add Bug', icon: <AddCircleOutlinedIcon /> },
    ...(canManageProjects ? [{ page: 'project-management', label: 'Projects', icon: <FolderCopyIcon /> }] : []),
    ...(canManageUsers ? [{ page: 'user-management', label: 'Users', icon: <PeopleAltIcon /> }] : []),
    ...(canManageUsers ? [{ page: 'audit-logs', label: 'Logs', icon: <ReceiptLongIcon /> }] : [])
  ];

  useEffect(() => {
    if (!token) {
      return undefined;
    }

    let isActive = true;
    fetchNotifications(token, { unreadOnly: true })
      .then((items) => {
        if (!isActive) {
          return;
        }
        setNotifications(Array.isArray(items) ? items : []);
        setNotificationsAvailable(true);
        setNotificationError('');
      })
      .catch(() => {
        if (!isActive) {
          return;
        }
        setNotifications([]);
        setNotificationsAvailable(false);
      });

    return () => {
      isActive = false;
    };
  }, [token]);

  function handleNavigate(page) {
    onNavigate(page);
    setMobileOpen(false);
  }

  async function handleMarkRead(notificationId) {
    setNotificationError('');
    try {
      await markNotificationRead(token, notificationId);
      setNotifications((current) => current.filter((item) => item.id !== notificationId));
      setNotificationAnchor(null);
    } catch {
      setNotificationError('Unable to mark notification read.');
    }
  }

  async function handleMarkAllRead() {
    setNotificationError('');
    try {
      await markAllNotificationsRead(token);
      setNotifications([]);
      setNotificationAnchor(null);
    } catch {
      setNotificationError('Unable to mark notifications read.');
    }
  }

  const drawer = (
    <Stack sx={{ height: '100%' }}>
      <Box sx={{ px: 2.5, py: 2.25 }}>
        <Stack direction="row" spacing={1.25} sx={{ alignItems: 'center' }}>
          <Avatar sx={{ bgcolor: 'primary.main', color: 'primary.contrastText', fontWeight: 900 }}>BT</Avatar>
          <Box>
            <Typography variant="subtitle1" sx={{ fontWeight: 900, lineHeight: 1.1 }}>Bug Ops</Typography>
            <Typography variant="caption" color="text.secondary">Incident command</Typography>
          </Box>
        </Stack>
      </Box>
      <Divider />
      <List aria-label="Main navigation" sx={{ px: 1.25, py: 1.5 }}>
        {navigationItems.map((item) => (
          <ListItemButton
            key={item.page}
            selected={currentPage === item.page}
            onClick={() => handleNavigate(item.page)}
            sx={{ borderRadius: 3, mb: 0.5 }}
          >
            <ListItemIcon sx={{ minWidth: 40 }}>{item.icon}</ListItemIcon>
            <ListItemText primary={<Typography sx={{ fontWeight: currentPage === item.page ? 900 : 700 }}>{item.label}</Typography>} />
          </ListItemButton>
        ))}
      </List>
      <Box sx={{ flexGrow: 1 }} />
      <Divider />
      <Stack spacing={1} sx={{ p: 2 }}>
        <Typography variant="caption" color="text.secondary">Signed in as</Typography>
        <Typography className="identity-tag" variant="body2" sx={{ fontWeight: 900, wordBreak: 'break-word' }}>{`${user?.username || user?.userId || 'user'} - ${user?.role || 'user'}`}</Typography>
        <Button type="button" startIcon={<LogoutIcon />} onClick={onLogout} color="inherit" variant="outlined">
          Log Out
        </Button>
      </Stack>
    </Stack>
  );

  return (
    <>
      <AppBar position="fixed" elevation={0} sx={{ zIndex: (value) => value.zIndex.drawer + 1, borderBottom: 1, borderColor: 'divider', backdropFilter: 'blur(16px)', bgcolor: 'background.paper' }}>
        <Toolbar>
          <IconButton
            type="button"
            color="inherit"
            edge="start"
            aria-label="Toggle navigation menu"
            aria-expanded={mobileOpen}
            onClick={() => setMobileOpen((value) => !value)}
            sx={{ mr: 1.5, display: { md: 'none' } }}
          >
            <MenuIcon />
          </IconButton>
          <Typography variant="h6" component="div" sx={{ flexGrow: 1, fontWeight: 900 }}>
            Bug Tracker
          </Typography>
          <ColorModeToggle />
          {notificationsAvailable ? (
            <IconButton
              type="button"
              color="inherit"
              aria-expanded={notificationsOpen}
              aria-label={`Notifications, ${notifications.length} unread`}
              onClick={(event) => setNotificationAnchor(event.currentTarget)}
            >
              <Badge badgeContent={notifications.length} color="error">
                <NotificationsIcon />
              </Badge>
            </IconButton>
          ) : null}
        </Toolbar>
      </AppBar>

      <Box component="nav" aria-label="Main navigation" sx={{ width: { md: drawerWidth }, flexShrink: { md: 0 } }}>
        <Drawer
          variant="temporary"
          open={mobileOpen && !desktop}
          onClose={() => setMobileOpen(false)}
          sx={{ display: { xs: 'block', md: 'none' }, '& .MuiDrawer-paper': { width: drawerWidth } }}
        >
          {drawer}
        </Drawer>
        <Drawer
          variant="permanent"
          open
          sx={{ display: { xs: 'none', md: 'block' }, '& .MuiDrawer-paper': { width: drawerWidth, boxSizing: 'border-box' } }}
        >
          {drawer}
        </Drawer>
      </Box>

      <Menu anchorEl={notificationAnchor} open={notificationsOpen} onClose={() => setNotificationAnchor(null)} slotProps={{ list: { 'aria-label': 'Unread notifications' } }}>
        {notificationError ? <MenuItem disabled>{notificationError}</MenuItem> : null}
        {notifications.length === 0 ? <MenuItem disabled>No unread alerts.</MenuItem> : null}
        {notifications.length > 0 ? (
          <MenuItem onClick={handleMarkAllRead} sx={{ fontWeight: 800 }}>
            Mark all read
          </MenuItem>
        ) : null}
        {notifications.map((notification) => (
          <MenuItem key={notification.id} sx={{ whiteSpace: 'normal', maxWidth: 360, alignItems: 'flex-start', gap: 1 }}>
            <Box sx={{ flexGrow: 1 }}>{notification.message || notification.title || 'Ticket notification'}</Box>
            <Button type="button" size="small" variant="text" onClick={() => handleMarkRead(notification.id)}>Mark read</Button>
          </MenuItem>
        ))}
      </Menu>
    </>
  );
}
