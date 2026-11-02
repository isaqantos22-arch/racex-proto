using System;

namespace RaceX.Career
{
    /// <summary>Textos do mundo vivo (PT-BR, próprios do RaceX): jornalistas, perguntas da imprensa,
    /// manchetes com variações, posts dos fãs e nomes. Cada manchete tem 3–4 versões para não repetir.</summary>
    public static class CareerWorldText
    {
        // ---------------- Nomes ----------------
        public static readonly string[] Initials = { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "R", "S", "T", "V", "Y" };

        /// <summary>Sobrenome + nacionalidade (sigla) — base dos pilotos inventados.</summary>
        public static readonly string[] Surnames =
        {
            "Almeida|BRA", "Barreto|BRA", "Cavalcanti|BRA", "Prado|BRA", "Teixeira|BRA", "Saldanha|BRA", "Moura|BRA", "Pacheco|BRA",
            "Holt|GBR", "Ashworth|GBR", "Pemberton|GBR", "Whitlock|GBR", "Carver|GBR", "Lowe|GBR",
            "Bertrand|FRA", "Lefèvre|FRA", "Moreau|FRA", "Garnier|FRA",
            "Brandt|DEU", "Vogel|DEU", "Krause|DEU", "Engel|DEU", "Hartmann|DEU",
            "Ricci|ITA", "Galli|ITA", "Conti|ITA", "Ferraro|ITA", "Leone|ITA",
            "Ibarra|ESP", "Navarro|ESP", "Cortés|ESP", "Soler|ESP",
            "Janssen|NLD", "de Wit|NLD", "Visser|NLD",
            "Takeda|JPN", "Mori|JPN", "Okada|JPN", "Hayashi|JPN",
            "Kowalski|POL", "Novák|CZE", "Lindgren|SWE", "Halvorsen|NOR", "Virtanen|FIN", "Mäkelä|FIN",
            "Ortega|MEX", "Ramírez|MEX", "Quintero|COL", "Benítez|ARG", "Sosa|ARG", "Cárdenas|CHL",
            "Kim|KOR", "Chen|CHN", "Zhou|CHN", "Rao|IND", "Mehta|IND",
            "Walsh|IRL", "Murray|AUS", "Kerr|NZL", "Dawson|CAN", "Tremblay|CAN", "Brooks|USA", "Hayes|USA", "Reyes|USA",
            "Petrescu|ROU", "Dimitrov|BGR", "Kovač|HRV", "Szabó|HUN", "Aydın|TUR", "Haddad|LBN", "Mansour|EGY", "Okafor|NGA"
        };

        /// <summary>Nacionalidade dos 23 rivais históricos da carreira (mesma ordem de CareerMode.RivalNames).</summary>
        public static readonly string[] RivalNats =
        {
            "BRA", "JPN", "BRA", "SWE", "BRA", "BRA", "ITA", "NGA", "DNK", "ITA", "ESP",
            "POR", "FIN", "FRA", "ESP", "ITA", "JPN", "ITA", "DEU", "GHA", "DEU", "FRA", "BRA"
        };

        public static readonly string[] PeopleFirst = { "Ana", "Bruno", "Carla", "Diego", "Elena", "Fábio", "Giulia", "Henrik", "Inês", "Jonas", "Kenji", "Lúcia", "Marco", "Nadia", "Otto", "Paula", "Rafael", "Sofia", "Tomás", "Vera" };
        public static readonly string[] PeopleLast = { "Rossi", "Lang", "Duarte", "Weber", "Moreau", "Silva", "Kaplan", "Novak", "Haas", "Lima", "Brennan", "Ishida", "Varga", "Costa", "Falk", "Rocha" };

        /// <summary>Nomes de quem COMPRA uma equipe falida (renome). Usados uma vez cada.</summary>
        public static readonly string[] BuyoutNames =
        {
            "Aurora Racing", "Tempesta GP", "Kraken Motorsport", "Atlas F1 Team", "Sirius Racing", "Condor Grand Prix",
            "Valkyrie Racing", "Orion Motorsport", "Phoenix Racing", "Titã Racing", "Boreal GP", "Carcará Racing"
        };

        // ---------------- Imprensa ----------------
        /// <summary>12 jornalistas fixos: nome | veículo | jeito de perguntar.</summary>
        public static readonly string[] Journalists =
        {
            "Marta Guedes|Pit Lane Brasil|adora uma polêmica",
            "Henrique Sá|Revista Grid|sempre ouve os dois lados",
            "Lívia Okamoto|Telemetria News|vive nos números",
            "Otávio Brandão|O Box|da velha guarda, difícil de impressionar",
            "Priscila Nunes|Autoesporte Mundo|acha seu rival melhor e diz isso",
            "Caio Werneck|Bandeirada|publica a frase antes de checar",
            "Júlia Fontes|Curva 1|sua maior fã na sala de imprensa",
            "Raul Esteves|Le Paddock (ed. BR)|gentil ao vivo, cruel no texto",
            "Tânia Cho|GP Digest|faz a pergunta que ninguém faz",
            "Beto Lacerda|Rádio Pista|leve e rápido",
            "Sílvia Marques|Fio do Paddock|só acredita quando se repete",
            "Ícaro Lund|Motor Nórdico|ainda lembra daquela que você perdeu"
        };

        /// <summary>Pergunta por situação. Cada uma com 3 respostas: texto | tom (0 ousado,1 humilde,2 provocador,3 leal,4 frio) | promessa.</summary>
        public static string Question(string situation, string teammate, string rival, out string[] options, out int[] tones, out string[] promises)
        {
            switch (situation)
            {
                case "vitoria":
                    options = new[] { "\"É só o começo.\"", "\"Mérito de toda a equipe.\"", "\"Vou vencer a próxima também.\"" };
                    tones = new[] { 0, 3, 0 }; promises = new[] { "", "", "vencer" };
                    return PickFresh(new[] { "Vitória! Qual o recado para o paddock?", "Que corrida. O que fez a diferença hoje?", "Você venceu com autoridade. Dá para repetir?",
                        "A largada definiu a corrida ou foi a estratégia?", "Em que momento você soube que a vitória era sua?", "Para quem vai essa vitória?",
                        "O carro estava perfeito ou foi você que tirou o máximo dele?", "Os rivais dizem que foi sorte. O que responde?" }, situation);
                case "podio":
                    options = new[] { "\"Pódio não é o objetivo, vitória é.\"", "\"Ótimo resultado para o time.\"", "\"Trabalho feito. Próxima.\"" };
                    tones = new[] { 0, 3, 4 }; promises = new[] { "podio", "", "" };
                    return PickFresh(new[] { "Um pódio sólido. Satisfeito?", "Pódio de novo. Falta o quê para vencer?", "Bom fim de semana. Qual foi a chave?",
                        "Faltou volta ou faltou carro para brigar pela vitória?", "O pneu aguentou do jeito que vocês planejaram?", "Esse troféu muda alguma coisa no campeonato?",
                        "A disputa no fim foi limpa?", "O que você vai pedir para a equipe melhorar até a próxima?" }, situation);
                case "acima":
                    options = new[] { "\"Às vezes o piloto faz a diferença.\"", "\"Tudo se encaixou com a equipe.\"", "\"Dia bom. Sem empolgação.\"" };
                    tones = new[] { 2, 3, 4 }; promises = new[] { "", "", "" };
                    return PickFresh(new[] { "Você andou muito acima do carro hoje. Como?", "Ninguém esperava esse resultado com esse carro. Explica?", "Foi o carro ou foi você?",
                        "As equipes grandes estão olhando para você. Está ouvindo propostas?", "Esse resultado vale como uma vitória para a sua equipe?", "Onde você ganhou mais tempo hoje?",
                        "Dá para repetir isso na próxima pista?" }, situation);
                case "abandono":
                    options = new[] { "\"Faz parte. Seguimos.\"", "\"Foi erro meu. Vou consertar.\"", "\"O carro me deixou na mão de novo.\"" };
                    tones = new[] { 4, 1, 2 }; promises = new[] { "", "", "" };
                    return PickFresh(new[] { "Abandono. Qual a sua reação?", "Fim de semana perdido. O que aconteceu?", "Mais um zero. Preocupado?",
                        "Você já viu o replay da batida? O que deu errado?", "A equipe tem um carro inteiro para consertar. Como fica o clima na garagem?",
                        "Foi excesso de confiança?", "Os comissários vão analisar o incidente. Você se sente culpado?", "Como você está fisicamente depois do impacto?",
                        "Esses zeros pesam no campeonato. Dá para recuperar?", "Algum recado para quem diz que você arrisca demais?" }, situation);
                case "companheiro":
                    options = new[] { "\"Mérito do " + teammate + ". Boa corrida.\"", "\"Me dá o mesmo carro e eu mostro.\"", "\"Vou terminar o ano na frente dele.\"" };
                    tones = new[] { 1, 2, 0 }; promises = new[] { "", "", "frente" };
                    return PickFresh(new[] { teammate + " foi melhor hoje. Sua opinião?", "O " + teammate + " está te superando. Incomoda?", "Na mesma equipe, " + teammate + " chegou na frente. E agora?",
                        "A equipe deu prioridade de estratégia para " + teammate + "?", "Os dados mostram " + teammate + " mais rápido nas curvas lentas. Já olhou a telemetria?",
                        "Existe clima ruim entre você e " + teammate + "?" }, situation);
                case "rival":
                    options = new[] { "\"Respeito, mas vou passar por ele.\"", "\"Não penso nele.\"", "\"Ele que se preocupe comigo.\"" };
                    tones = new[] { 0, 4, 2 }; promises = new[] { "", "", "" };
                    return PickFresh(new[] { "Mais uma briga com " + rival + ". Tem algo pessoal?", rival + " disse que você exagera nas disputas. Responde?", "Você e " + rival + " de novo na mesma curva. Vai acabar mal?",
                        "Vocês já conversaram depois daquela disputa?", rival + " diz que você não deixa espaço. Concorda?", "Essa rivalidade é boa para o campeonato?" }, situation);
                default: // fora dos pontos
                    options = new[] { "\"Vamos voltar mais fortes.\"", "\"Fim de semana difícil, aprendemos.\"", "\"Vou pontuar na próxima, pode anotar.\"" };
                    tones = new[] { 0, 1, 0 }; promises = new[] { "", "", "podio" };
                    return PickFresh(new[] { "Fora dos pontos. O que faltou?", "Fim de semana para esquecer?", "O ritmo não veio. Por quê?",
                        "A estratégia de pneus funcionou?", "O tráfego atrapalhou mais do que o esperado?", "O que a classificação deixou de fazer por você?",
                        "Você está satisfeito com o acerto do carro?", "Qual é a meta para a próxima corrida?" }, situation);
            }
        }

        // ---------------- Fãs (rede social) ----------------
        public static string FanPost(float sentiment, float form, int following, string name, string promise, int salt)
        {
            string f = following >= 1000 ? (following / 1000f).ToString("0.#") + " mil" : following.ToString();
            string[] pool;
            if (!string.IsNullOrEmpty(promise) && salt % 3 == 0)
                pool = new[] { "ainda esperando aquele \"" + promise + "\" do " + name + ", não esquecemos", name + " prometeu: \"" + promise + "\". a gente guardou o print" };
            else if (form >= 0.5f)
                pool = new[] { name + " na melhor fase, vídeo dele em todo lugar", "o " + name + " tá voando e ninguém comenta", "fã do " + name + " tá comendo bem essa semana" };
            else if (form <= -0.5f)
                pool = new[] { name + " sumido do ritmo, os comentários estão pesados", "fase difícil pro " + name + ", alguém dá um abraço", name + " precisa de um reset, fato" };
            else if (sentiment >= 0.68f)
                pool = new[] { "para de hate com o " + name + ", é pilotagem boa de verdade", name + " é o piloto mais simpático do grid e ponto", "torcendo pelo " + name + " desde o primeiro dia" };
            else if (sentiment <= 0.35f)
                pool = new[] { name + " de novo? que preguiça", "não aguento mais desculpa do " + name, "o " + name + " fala demais e entrega de menos" };
            else
                pool = new[] { name + "? sinceramente, tô dividido", "uns amam o " + name + ", outros odeiam, não tem meio-termo", "ainda não decidi o que acho do " + name };
            string post = pool[Math.Abs(salt) % pool.Length];
            return salt % 2 == 0 ? post : post + "  ·  " + f + " seguidores";
        }

        // ---------------- Manchetes do mundo ----------------
        /// <summary>Novato em destaque: manchete + texto variados (antes era a MESMA frase para 6 pilotos todo fim de semana).</summary>
        public static void RookieNews(string name, string team, int pos, out string headline, out string body)
        {
            int i = PickIndex(10, "rookie");
            string p = pos == 1 ? "a vitória" : "o " + pos + "º lugar";
            switch (i)
            {
                case 0: headline = "O novato que chamou a atenção: " + name; body = name + " levou a " + team + " a " + p + " e os chefes de equipe já perguntam por um contrato mais longo."; break;
                case 1: headline = name + " assusta os veteranos"; body = "Com poucas corridas na categoria, " + name + " chegou em " + p.Replace("a vitória", "1º") + ". No paddock, a pergunta é até onde ele chega com um carro melhor."; break;
                case 2: headline = "Estreante, mas não parece"; body = name + " conduziu a corrida com calma de veterano e garantiu " + p + " para a " + team + ". O engenheiro de corrida falou em \"maturidade rara\"."; break;
                case 3: headline = "A " + team + " encontrou uma joia?"; body = "O resultado de " + name + " (" + p + ") mexeu com o mercado. Empresários de outras equipes foram vistos na garagem depois da bandeirada."; break;
                case 4: headline = name + ": de reserva a destaque"; body = "Há poucos meses ninguém falava de " + name + ". Hoje, " + p + " e o nome na lista de todo chefe de equipe."; break;
                case 5: headline = "Pódio precoce para " + name; body = "Nenhuma das primeiras corridas de " + name + " tinha passado do meio do pelotão. Desta vez, " + p + " — e uma volta mais rápida que a do companheiro."; break;
                case 6: headline = "Os números de " + name + " impressionam"; body = "Dados de setor mostram " + name + " entre os três mais rápidos nas curvas de alta. Com " + p + ", a " + team + " já fala em renovação."; break;
                case 7: headline = "Primeiro grande resultado de " + name; body = name + " comemorou " + p + " com a equipe toda no muro dos boxes. \"Era o que a gente precisava\", disse o chefe da " + team + "."; break;
                case 8: headline = "Novo nome no topo"; body = "A categoria tem mais um candidato a estrela: " + name + ", " + p + " em uma das corridas mais disputadas do ano."; break;
                default: headline = name + " ganha confiança"; body = "Depois de um começo irregular, " + name + " encaixou o fim de semana e terminou em " + p + ". A " + team + " fala em evolução constante."; break;
            }
        }

        // ---------------- Utilitários ----------------
        /// <summary>Variação NOVA: não repete uma já usada (memória gravada no save) até esgotar todas; depois recomeça.</summary>
        public static string PickFresh(string[] variants, string key)
        {
            if (variants == null || variants.Length == 0) return "";
            return variants[PickIndex(variants.Length, key)];
        }

        public static int PickIndex(int count, string key)
        {
            var w = CareerWorld.W;
            if (w == null || count <= 1) return Math.Abs((key ?? "").GetHashCode() + seq++) % Math.Max(1, count);
            if (w.usedText == null) w.usedText = new System.Collections.Generic.List<string>();
            var free = new System.Collections.Generic.List<int>();
            for (int i = 0; i < count; i++) if (!w.usedText.Contains(key + "|" + i)) free.Add(i);
            if (free.Count == 0)
            {
                w.usedText.RemoveAll(x => x.StartsWith(key + "|"));
                for (int i = 0; i < count; i++) free.Add(i);
            }
            var rnd = new Random((key ?? "").GetHashCode() ^ (w.news != null ? w.news.Count * 7919 : 0) ^ (seq++ * 104729));
            int pick = free[rnd.Next(free.Count)];
            w.usedText.Add(key + "|" + pick);
            if (w.usedText.Count > 600) w.usedText.RemoveRange(0, 100);
            return pick;
        }
        static int seq;
        /// <summary>Escolhe uma variação (rotaciona por chamada + chave): o mesmo texto não sai duas vezes seguidas.</summary>
        public static string Pick(string[] variants, string key)
        {
            if (variants == null || variants.Length == 0) return "";
            int h = 17;
            foreach (char c in key ?? "") h = h * 31 + c;
            seq++;
            return variants[Math.Abs(h + seq * 7) % variants.Length];
        }
    }
}
