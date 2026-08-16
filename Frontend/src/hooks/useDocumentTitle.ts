/**
 * useDocumentTitle - set the browser tab title
 */
import { useEffect } from 'react';

export function useDocumentTitle(title: string) {
  useEffect(() => {
    const prev = document.title;
    document.title = `${title} | AttackShield AI`;
    return () => {
      document.title = prev;
    };
  }, [title]);
}
