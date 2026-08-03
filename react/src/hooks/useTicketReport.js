import { useCallback, useEffect, useRef, useState } from 'react';
import { addBugComment, fetchBugById } from '../api/bugs';
import { readAppUrlState, writeAppUrlState } from '../url_state';

export default function useTicketReport(token) {
  const [ticket, setTicket] = useState(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const mountedRef = useRef(true);
  const requestRef = useRef(0);

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
      requestRef.current += 1;
    };
  }, []);

  const closeReport = useCallback(() => {
    requestRef.current += 1;
    setTicket(null);
    setLoading(false);
    setError('');
    writeAppUrlState({ ticket: '' }, { replace: true });
  }, []);

  const openReport = useCallback(async (summary) => {
    if (!summary?.id) return;

    const requestId = requestRef.current + 1;
    requestRef.current = requestId;
    setTicket(null);
    setError('');
    setLoading(true);
    writeAppUrlState({ ...readAppUrlState(), ticket: summary.id }, { replace: true });

    try {
      const fullTicket = await fetchBugById(token, summary.id);
      if (mountedRef.current && requestRef.current === requestId) {
        setTicket(fullTicket);
      }
    } catch (err) {
      if (mountedRef.current && requestRef.current === requestId) {
        setError(err);
      }
    } finally {
      if (mountedRef.current && requestRef.current === requestId) {
        setLoading(false);
      }
    }
  }, [token]);

  const addComment = useCallback(async (ticketId, body, recipientUserId = '') => {
    const reportRequestId = requestRef.current;
    const comment = await addBugComment(token, ticketId, body, recipientUserId);
    if (mountedRef.current && requestRef.current === reportRequestId) {
      setTicket((current) => current?.id === ticketId
        ? { ...current, activity: [comment, ...(current.activity || [])] }
        : current);
    }
    return comment;
  }, [token]);

  return {
    ticket,
    loading,
    error,
    isOpen: Boolean(ticket || loading || error),
    openReport,
    closeReport,
    addComment
  };
}
