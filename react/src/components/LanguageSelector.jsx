import React, { useState } from 'react';
import IconButton from '@mui/material/IconButton';
import Menu from '@mui/material/Menu';
import MenuItem from '@mui/material/MenuItem';
import Typography from '@mui/material/Typography';
import { useI18n } from '../i18n';

export default function LanguageSelector() {
  const { language, setLanguage, t } = useI18n();
  const [anchor, setAnchor] = useState(null);
  const open = Boolean(anchor);
  const flag = language === 'fr-FR' ? '🇫🇷' : '🇬🇧';

  function choose(nextLanguage) {
    setLanguage(nextLanguage);
    setAnchor(null);
  }

  return (
    <>
      <IconButton aria-label={t('language.label', 'Language')} aria-haspopup="menu" aria-expanded={open} onClick={(event) => setAnchor(event.currentTarget)}>
        <span aria-hidden="true" style={{ fontSize: '1.25rem', lineHeight: 1 }}>{flag}</span>
      </IconButton>
      <Menu anchorEl={anchor} open={open} onClose={() => setAnchor(null)} slotProps={{ list: { 'aria-label': t('language.label', 'Language') } }}>
        <MenuItem selected={language === 'en-GB'} onClick={() => choose('en-GB')}>
          <Typography component="span" sx={{ mr: 1 }} aria-hidden="true">🇬🇧</Typography>{t('language.english', 'English')}
        </MenuItem>
        <MenuItem selected={language === 'fr-FR'} onClick={() => choose('fr-FR')}>
          <Typography component="span" sx={{ mr: 1 }} aria-hidden="true">🇫🇷</Typography>{t('language.french', 'French')}
        </MenuItem>
      </Menu>
    </>
  );
}
