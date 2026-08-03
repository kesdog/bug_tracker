import { createTheme } from '@mui/material/styles';

export const statusColors = {
  todo: 'info',
  open: 'primary',
  reopened: 'warning',
  closed: 'success',
  cancelled: 'warning'
};

export const severityColors = {
  low: 'success',
  mid: 'info',
  high: 'warning',
  urgent: 'error'
};

export const priorityColors = {
  p0: 'error',
  p1: 'warning',
  p2: 'info',
  p3: 'success'
};

export const tagPalette = ['#16a34a', '#0891b2', '#2563eb', '#7c3aed', '#db2777', '#ea580c'];
export const defaultColorMode = 'dark';

export function getTagColor(tag = '') {
  const value = String(tag);
  const hash = Array.from(value).reduce((total, char) => total + char.charCodeAt(0), 0);
  return tagPalette[hash % tagPalette.length];
}

export const appTheme = createTheme({
  cssVariables: { colorSchemeSelector: 'class' },
  colorSchemes: {
    light: {
      palette: {
        primary: { main: '#4b5563' },
        secondary: { main: '#6b7280' },
        background: { default: '#f7f7f8', paper: '#fbfbfc' },
        divider: 'rgba(75, 85, 99, 0.16)',
        text: { primary: '#111827', secondary: '#4b5563' }
      }
    },
    dark: {
      palette: {
        primary: { main: '#7dd3fc' },
        secondary: { main: '#5eead4' },
        background: { default: '#07111f', paper: '#101b2d' },
        divider: 'rgba(148, 163, 184, 0.22)',
        text: { primary: '#f8fafc', secondary: '#cbd5e1' }
      }
    }
  },
  shape: { borderRadius: 14 },
  typography: {
    fontFamily: ['IBM Plex Sans', 'Aptos', 'Segoe UI', 'sans-serif'].join(','),
    h1: { fontWeight: 800, letterSpacing: '-0.04em' },
    h2: { fontWeight: 800, letterSpacing: '-0.035em' },
    button: { fontWeight: 800, textTransform: 'none' }
  },
  components: {
    MuiCssBaseline: {
      styleOverrides: (theme) => ({
        body: {
          minHeight: '100vh',
          backgroundColor: theme.vars.palette.background.default,
          backgroundImage:
            'radial-gradient(circle at 8% 8%, rgba(75, 85, 99, 0.10), transparent 24rem), radial-gradient(circle at 92% 12%, rgba(156, 163, 175, 0.14), transparent 22rem)',
          ...theme.applyStyles('dark', {
            backgroundImage:
              'radial-gradient(circle at 10% 8%, rgba(125, 211, 252, 0.18), transparent 24rem), radial-gradient(circle at 92% 12%, rgba(94, 234, 212, 0.13), transparent 22rem), linear-gradient(135deg, #07111f 0%, #0b1627 52%, #050914 100%)'
          })
        },
        '#root': { minHeight: '100vh' },
        'button, input, textarea, select': { font: 'inherit' }
      })
    },
    MuiAppBar: {
      styleOverrides: {
        root: ({ theme }) => ({
          color: theme.vars.palette.text.primary,
          backgroundImage: 'linear-gradient(180deg, rgba(255,255,255,0.86), rgba(255,255,255,0.72))',
          ...theme.applyStyles('dark', {
            backgroundImage: 'linear-gradient(180deg, rgba(16, 27, 45, 0.86), rgba(16, 27, 45, 0.68))',
            boxShadow: '0 18px 40px rgba(2, 6, 23, 0.22)'
          })
        })
      }
    },
    MuiCard: {
      styleOverrides: {
        root: ({ theme }) => ({
          border: `1px solid ${theme.vars.palette.divider}`,
          backgroundImage: 'linear-gradient(180deg, rgba(255,255,255,0.96), rgba(255,255,255,0.88))',
          boxShadow: '0 18px 48px rgba(15, 23, 42, 0.10)',
          ...theme.applyStyles('dark', {
            backgroundImage: 'linear-gradient(180deg, rgba(16, 27, 45, 0.96), rgba(11, 22, 39, 0.92))',
            boxShadow: '0 24px 70px rgba(2, 6, 23, 0.46)'
          })
        })
      }
    },
    MuiPaper: {
      styleOverrides: {
        root: ({ theme }) => ({
          backgroundImage: `linear-gradient(180deg, ${theme.vars.palette.background.paper}, ${theme.vars.palette.background.paper})`,
          ...theme.applyStyles('dark', {
            backgroundImage: 'linear-gradient(180deg, rgba(16, 27, 45, 0.98), rgba(10, 19, 35, 0.96))'
          })
        })
      }
    },
    MuiButton: {
      defaultProps: { variant: 'contained' },
      styleOverrides: {
        root: { borderRadius: 999 },
        contained: ({ theme }) => ({
          boxShadow: '0 12px 28px rgba(75, 85, 99, 0.18)',
          ...theme.applyStyles('dark', {
            boxShadow: '0 14px 32px rgba(14, 165, 233, 0.20)'
          })
        })
      }
    },
    MuiChip: {
      styleOverrides: { root: { fontWeight: 800, letterSpacing: '0.01em' } }
    },
    MuiDialog: {
      defaultProps: { fullWidth: true, transitionDuration: 0 },
      styleOverrides: {
        paper: ({ theme }) => ({
          border: `1px solid ${theme.vars.palette.divider}`,
          boxShadow: '0 24px 80px rgba(2, 6, 23, 0.28)',
          ...theme.applyStyles('dark', {
            backgroundImage: 'linear-gradient(180deg, rgba(16, 27, 45, 0.98), rgba(8, 16, 31, 0.98))',
            boxShadow: '0 24px 90px rgba(0, 0, 0, 0.58)'
          })
        })
      }
    },
    MuiDrawer: {
      defaultProps: { transitionDuration: 0 },
      styleOverrides: {
        paper: ({ theme }) => ({
          borderColor: theme.vars.palette.divider,
          ...theme.applyStyles('dark', {
            backgroundImage: 'linear-gradient(180deg, rgba(15, 23, 42, 0.98), rgba(7, 17, 31, 0.98))'
          })
        })
      }
    },
    MuiMenu: {
      defaultProps: { transitionDuration: 0 }
    },
    MuiOutlinedInput: {
      styleOverrides: {
        root: ({ theme }) => ({
          backgroundColor: 'rgba(255, 255, 255, 0.72)',
          ...theme.applyStyles('dark', {
            backgroundColor: 'rgba(15, 23, 42, 0.52)'
          })
        })
      }
    },
    MuiListItemButton: {
      styleOverrides: {
        root: ({ theme }) => ({
          '&.Mui-selected': {
            backgroundColor: 'rgba(75, 85, 99, 0.12)',
            color: theme.vars.palette.primary.main,
            '& .MuiListItemIcon-root': { color: theme.vars.palette.primary.main },
            '&:hover': { backgroundColor: 'rgba(75, 85, 99, 0.18)' }
          },
          ...theme.applyStyles('dark', {
            '&.Mui-selected': {
              backgroundColor: 'rgba(125, 211, 252, 0.14)',
              '&:hover': { backgroundColor: 'rgba(125, 211, 252, 0.20)' }
            }
          })
        })
      }
    }
  }
});
