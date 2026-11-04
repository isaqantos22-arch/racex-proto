-- RaceX v99: aba GLOBAL (recordes de volta e ranking de pilotos) no Supabase (projeto loyxhoowgonlgrlrykrd).
-- Executar UMA vez no SQL Editor do Supabase. Não mexe na tabela profiles (só lê nome/estatísticas pela função).

-- Melhor volta de cada conta em cada pista com cada carro, por versão da física (99 = pneu µ 1,8; tempos antigos não se misturam).
create table if not exists public.lap_records (
  user_id   uuid not null references auth.users(id) on delete cascade,
  track     int  not null,                  -- TrackId do jogo (0..23)
  car       text not null,                  -- modelo (enum do jogo) ou "F2" (carro único da carreira)
  car_name  text not null default '',
  category  text not null default '',       -- GT / F1 / F2
  lap_ms    int  not null check (lap_ms between 10000 and 1800000),
  compound  int  not null default -1,
  wet       boolean not null default false,
  physics   int  not null default 99,
  name      text not null default 'Jogador',
  set_at    timestamptz not null default now(),
  primary key (user_id, track, car, physics)
);
create index if not exists lap_records_board on public.lap_records (track, physics, lap_ms);

alter table public.lap_records enable row level security;
drop policy if exists "recordes publicos" on public.lap_records;
create policy "recordes publicos" on public.lap_records for select using (true);
-- Sem policy de insert/update: a escrita é só pela função abaixo (a conta grava SÓ o próprio tempo, e só se melhorar).

create or replace function public.submit_lap(p_track int, p_car text, p_car_name text, p_category text, p_lap_ms int,
                                             p_compound int, p_wet boolean, p_physics int)
returns void language plpgsql security definer set search_path = public as $$
declare uid uuid := auth.uid(); nm text;
begin
  if uid is null then raise exception 'not authenticated'; end if;
  if p_lap_ms < 10000 or p_lap_ms > 1800000 or p_track < 0 or p_track > 63 then raise exception 'invalid lap'; end if;
  select coalesce(nullif(name, ''), 'Jogador') into nm from public.profiles where id = uid;
  insert into public.lap_records (user_id, track, car, car_name, category, lap_ms, compound, wet, physics, name, set_at)
  values (uid, p_track, left(p_car, 40), left(coalesce(p_car_name, ''), 60), left(coalesce(p_category, ''), 8), p_lap_ms,
          p_compound, p_wet, p_physics, coalesce(nm, 'Jogador'), now())
  on conflict (user_id, track, car, physics) do update
    set lap_ms = excluded.lap_ms, car_name = excluded.car_name, category = excluded.category, compound = excluded.compound,
        wet = excluded.wet, name = excluded.name, set_at = now()
    where excluded.lap_ms < public.lap_records.lap_ms;
end $$;
revoke all on function public.submit_lap(int, text, text, text, int, int, boolean, int) from public, anon;
grant execute on function public.submit_lap(int, text, text, text, int, int, boolean, int) to authenticated;

-- Ranking de pilotos: só nome, avatar de capacete (preset) e estatísticas — nunca e-mail, foto enviada ou save da carreira.
create or replace function public.global_players()
returns table(name text, photo text, avatar_color int, wins int, podiums int, titles int, races int, records int)
language sql security definer set search_path = public stable as $$
  select coalesce(nullif(p.name, ''), 'Jogador'),
         case when p.photo like 'preset:%' then p.photo else '' end,
         coalesce(p.avatar_color, 0), coalesce(p.wins, 0), coalesce(p.podiums, 0), coalesce(p.titles, 0), coalesce(p.races, 0),
         (select count(*)::int from public.lap_records r where r.user_id = p.id and r.physics = 99)
  from public.profiles p
  order by p.wins desc nulls last, p.podiums desc nulls last, p.races desc nulls last
  limit 100;
$$;
grant execute on function public.global_players() to anon, authenticated;
