import React from 'react';
import Button from '@mui/material/Button';
import DescriptionOutlinedIcon from '@mui/icons-material/DescriptionOutlined';
import { useI18n } from '../i18n';

export default function ViewReportsButton({ ticket, onOpen }) {
  const { t } = useI18n();
  return (
    <Button
      type="button"
      size="small"
      variant="outlined"
      startIcon={<DescriptionOutlinedIcon />}
      onClick={() => onOpen(ticket)}
      aria-label={t('tickets.actions.viewReportsFor', 'View reports for {{ticket}}', { ticket: ticket.issueTitle || t('tickets.singular', 'ticket') })}
    >
      {t('tickets.actions.viewReports', 'View Reports')}
    </Button>
  );
}
