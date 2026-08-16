import React, { useEffect, useState } from 'react';
import { HiIdentification, HiPlus, HiTrash, HiUserGroup } from 'react-icons/hi2';
import toast from 'react-hot-toast';
import { LoadingSpinner } from '@/components';
import { useDocumentTitle } from '@/hooks';
import { HitListAPI } from '@/services/api';
import type { HitListEntry } from '@/types';

async function prepareImage(file: File): Promise<string> {
  const source = await createImageBitmap(file);
  const scale = Math.min(1, 900 / Math.max(source.width, source.height));
  const canvas = document.createElement('canvas');
  canvas.width = Math.round(source.width * scale);
  canvas.height = Math.round(source.height * scale);
  canvas.getContext('2d')!.drawImage(source, 0, 0, canvas.width, canvas.height);
  source.close();
  return canvas.toDataURL('image/jpeg', 0.85);
}

export default function HitListPage() {
  useDocumentTitle('Hit List');
  const [entries, setEntries] = useState<HitListEntry[]>([]);
  const [name, setName] = useState('');
  const [notes, setNotes] = useState('');
  const [imageUrl, setImageUrl] = useState('');
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    void HitListAPI.list().then((response) => {
      if (response.success && response.data) setEntries(response.data);
      else toast.error(response.error || 'Failed to load hit list');
      setLoading(false);
    });
  }, []);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!name.trim() || !imageUrl) return;
    setSaving(true);
    const response = await HitListAPI.add({ name: name.trim(), imageUrl, notes: notes.trim() });
    if (response.success && response.data) {
      setEntries((current) => [response.data!, ...current]);
      setName('');
      setNotes('');
      setImageUrl('');
      toast.success('Person added to hit list');
    } else {
      toast.error(response.error || 'Failed to add person');
    }
    setSaving(false);
  };

  const remove = async (entry: HitListEntry) => {
    if (!window.confirm(`Remove ${entry.name} from the hit list?`)) return;
    const response = await HitListAPI.remove(entry.id);
    if (response.success) setEntries((current) => current.filter((item) => item.id !== entry.id));
    else toast.error(response.error || 'Failed to remove person');
  };

  if (loading) return <LoadingSpinner text="Loading Hit List..." />;

  return (
    <div className="p-4 lg:p-8 max-w-5xl mx-auto">
      <div className="flex items-center gap-3 mb-6">
        <HiIdentification className="text-red-400" size={28} />
        <h2 className="text-xl font-bold text-slate-800 dark:text-white">Hit List</h2>
        <span className="ml-auto text-sm text-slate-500">{entries.length} registered</span>
      </div>

      <form onSubmit={submit} className="card grid gap-4 md:grid-cols-[160px_1fr_auto] items-end mb-6">
        <label className="block cursor-pointer">
          <span className="text-xs font-medium text-slate-500">Reference photo</span>
          <div className="mt-2 h-32 w-full overflow-hidden rounded border border-dashed border-slate-500 flex items-center justify-center bg-slate-950/20">
            {imageUrl ? <img src={imageUrl} alt="Reference" className="h-full w-full object-cover" /> : <HiUserGroup size={32} className="text-slate-500" />}
          </div>
          <input
            className="sr-only"
            type="file"
            accept="image/jpeg,image/png,image/webp"
            onChange={async (event) => {
              const file = event.target.files?.[0];
              if (file) setImageUrl(await prepareImage(file));
            }}
          />
        </label>
        <div className="grid gap-3">
          <label className="text-xs font-medium text-slate-500">
            Person name
            <input className="input mt-1 w-full" value={name} onChange={(event) => setName(event.target.value)} maxLength={80} required />
          </label>
          <label className="text-xs font-medium text-slate-500">
            Notes
            <input className="input mt-1 w-full" value={notes} onChange={(event) => setNotes(event.target.value)} maxLength={250} />
          </label>
        </div>
        <button className="btn-primary flex items-center justify-center gap-2" disabled={saving || !imageUrl}>
          <HiPlus size={18} /> {saving ? 'Adding...' : 'Add person'}
        </button>
      </form>

      {entries.length === 0 ? (
        <div className="py-20 text-center text-slate-500"><HiUserGroup className="mx-auto mb-3" size={48} />No registered people</div>
      ) : (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {entries.map((entry) => (
            <article key={entry.id} className="card flex items-center gap-3">
              <img src={entry.imageUrl} alt={entry.name} className="h-16 w-16 rounded object-cover bg-black" />
              <div className="min-w-0 flex-1">
                <h3 className="truncate font-semibold text-slate-800 dark:text-white">{entry.name}</h3>
                <p className="truncate text-xs text-slate-500">{entry.notes || 'No notes'}</p>
              </div>
              <button type="button" onClick={() => void remove(entry)} className="p-2 text-red-400 hover:bg-red-500/10 rounded" title={`Remove ${entry.name}`}>
                <HiTrash size={18} />
              </button>
            </article>
          ))}
        </div>
      )}
    </div>
  );
}