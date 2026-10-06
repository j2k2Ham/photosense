import React from 'react';

export interface Toast { id: number; message: string; type?: 'info'|'success'|'error'; }

export function useToasts(){
  const [toasts,setToasts] = React.useState<Toast[]>([]);
  const remove = React.useCallback((id:number)=> setToasts(t=>t.filter(x=>x.id!==id)),[]);
  const push = React.useCallback((message: string, type: Toast['type']='info')=>{
    const id = Date.now()+Math.random();
    setToasts(t=>[...t,{id, message, type}]);
    // Errors stay until dismissed; everything else clears itself.
    if (type !== 'error') setTimeout(()=>remove(id), 8000);
  },[remove]);
  return { toasts, push, remove };
}

const toastColors: Record<NonNullable<Toast['type']>, string> = {
  info: 'bg-neutral-800 border-neutral-600',
  success: 'bg-emerald-900 border-emerald-500',
  error: 'bg-red-900 border-red-500',
};

interface ToasterProps { readonly toasts: Toast[]; readonly remove: (id:number)=>void; }
export function Toaster({ toasts, remove }: ToasterProps){
  return (
    <div className="fixed bottom-4 right-4 flex flex-col gap-2 z-50">
      {toasts.map(t=> (
     <button key={t.id} className={`text-left max-w-md px-3 py-2 rounded shadow text-sm border focus:outline-none focus:ring-2 ring-offset-0 ring-emerald-400 ${toastColors[t.type ?? 'info']}`}
       onClick={()=>remove(t.id)}>
          {t.message}
     </button>
      ))}
    </div>
  );
}