import React from 'react';
import Button from '@mui/material/Button';
import DescriptionOutlinedIcon from '@mui/icons-material/DescriptionOutlined';

export default function ViewReportsButton({ ticket, onOpen }) {
  return (
    <Button
      type="button"
      size="small"
      variant="outlined"
      startIcon={<DescriptionOutlinedIcon />}
      onClick={() => onOpen(ticket)}
      aria-label={`View reports for ${ticket.issueTitle || 'ticket'}`}
    >
      View Reports
    </Button>
  );
}
