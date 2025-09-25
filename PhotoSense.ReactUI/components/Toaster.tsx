import React from 'react';

export interface Toast { id: number; message: string; type?: 'info'|'success'|'error'; }

export function useToasts(){
  const [toasts,setToasts] = React.useState<Toast[]>([]);
  const push = React.useCallback((message: string, type: Toast['type']='info')=>{
    setToasts(t=>[...t,{id: Date.now()+Math.random(), message, type}]);
  },[]);
  const remove = React.useCallback((id:number)=> setToasts(t=>t.filter(x=>x.id!==id)),[]);
  return { toasts, push, remove };
}

interface ToasterProps { readonly toasts: Toast[]; readonly remove: (id:number)=>void; }
export function Toaster({ toasts, remove }: ToasterProps){
  return (
    <div className="fixed bottom-4 right-4 flex flex-col gap-2 z-50">
      {toasts.map(t=> (
     <button key={t.id} className={`text-left px-3 py-2 rounded shadow text-sm border focus:outline-none focus:ring-2 ring-offset-0 ring-emerald-400 ${t.type==='error'?'bg-red-800/80 border-red-500':'bg-neutral-800/90 border-neutral-600'} ${t.type==='success'?'bg-emerald-800/80 border-emerald-500':''}`}
       onClick={()=>remove(t.id)}>
          {t.message}
     </button>
      ))}
    </div>
  );
}