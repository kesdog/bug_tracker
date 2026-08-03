import React from 'react';
import { createRoot } from 'react-dom/client';
import createCache from '@emotion/cache';
import { CacheProvider } from '@emotion/react';
import CssBaseline from '@mui/material/CssBaseline';
import InitColorSchemeScript from '@mui/material/InitColorSchemeScript';
import { ThemeProvider } from '@mui/material/styles';
import App from './App';
import './styles/styles.css';
import { appTheme, defaultColorMode } from './theme';
import { I18nProvider } from './i18n';

const nonce = document.querySelector('meta[name="csp-nonce"]')?.getAttribute('content');
const emotionCache = createCache({ key: 'bug-tracker', nonce: nonce && nonce !== '__CSP_NONCE__' ? nonce : undefined, prepend: true });

createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <CacheProvider value={emotionCache}>
      <InitColorSchemeScript attribute="class" defaultMode={defaultColorMode} nonce={nonce && nonce !== '__CSP_NONCE__' ? nonce : undefined} />
      <ThemeProvider theme={appTheme} defaultMode={defaultColorMode} disableTransitionOnChange noSsr>
        <CssBaseline />
        <I18nProvider><App /></I18nProvider>
      </ThemeProvider>
    </CacheProvider>
  </React.StrictMode>
);
