using System;

namespace RaceX.Career
{
    /// <summary>Uma pergunta do quiz de personalidade do modo carreira.</summary>
    [Serializable]
    public class Question
    {
        public string prompt;
        public OptionWeights[] options;
    }

    /// <summary>Pesos de uma opção: estilo por eixo + arquétipo/academia dominantes.</summary>
    [Serializable]
    public class OptionWeights
    {
        public string text;
        public int attack;
        public int feel;
        public int stage;
        public int heat;
        public string archetype; // chave dominante: prodigy/aggressor/technician/media_darling/late_bloomer
        public string academy;   // chave dominante: ferrari_academy/red_bull_junior/mercedes_junior/renault_academy/independent
    }

    /// <summary>Dados fixos do quiz de personalidade (FONTE: Pit Wall 2.0.0/questions.json + textos PT).</summary>
    public static class CareerQuizData
    {
        public static readonly string[] StyleKeys = { "attack", "feel", "stage", "heat" };
        public static readonly string[] ArchetypeKeys = { "prodigy", "aggressor", "technician", "media_darling", "late_bloomer" };
        public static readonly string[] AcademyKeys = { "ferrari_academy", "red_bull_junior", "mercedes_junior", "renault_academy", "independent" };

        /// <summary>Nome PT do arquétipo.</summary>
        public static string ArchetypeName(string key)
        {
            switch (key)
            {
                case "prodigy": return "O Prodígio";
                case "aggressor": return "O Agressor";
                case "technician": return "O Técnico";
                case "media_darling": return "O Queridinho da Mídia";
                case "late_bloomer": return "O Florescer Tardio";
                default: return "O Prodígio";
            }
        }

        /// <summary>Descrição curta PT do arquétipo.</summary>
        public static string ArchetypeDescription(string key)
        {
            switch (key)
            {
                case "prodigy": return "Talento natural puro — velocidade imediata, sem esforço aparente.";
                case "aggressor": return "Ultrapassagens arrojadas e pressão constante sobre os adversários.";
                case "technician": return "Telemetria, acerto mecânico e consistência cirúrgica em cada detalhe.";
                case "media_darling": return "Carisma com fãs e patrocinadores — alto valor de mercado.";
                case "late_bloomer": return "Resiliente, aprende com os erros e atinge o auge com a experiência.";
                default: return "Talento natural puro — velocidade imediata, sem esforço aparente.";
            }
        }

        /// <summary>Nome da academia de juniores (F2).</summary>
        public static string AcademyName(string key)
        {
            switch (key)
            {
                case "ferrari_academy": return "Ferrari Driver Academy";
                case "red_bull_junior": return "Red Bull Junior Team";
                case "mercedes_junior": return "Mercedes Junior Team";
                case "renault_academy": return "Renault Sport Academy";
                case "independent": return "Independente";
                default: return "Independente";
            }
        }

        /// <summary>Equipe de F2 concedida pela academia.</summary>
        public static string AcademyTeam(string key)
        {
            switch (key)
            {
                case "ferrari_academy": return "Prema";
                case "red_bull_junior": return "Carlin";
                case "mercedes_junior": return "ART Grand Prix";
                case "renault_academy": return "Virtuosi";
                case "independent": return "Independente";
                default: return "Independente";
            }
        }

        /// <summary>As 14 perguntas do quiz (pesos exatos do questions.json, textos em PT).</summary>
        public static Question[] All =
        {
            new Question
            {
                prompt = "É a noite anterior ao seu primeiro teste de verdade. O que está passando pela sua cabeça?",
                options = new[]
                {
                    new OptionWeights { text = "Sinceramente? Já dei tantas voltas na minha cabeça que conseguiria fazer o circuito de olhos fechados.", attack = -1, feel = -2, stage = 0, heat = 0, archetype = "technician", academy = "ferrari_academy" },
                    new OptionWeights { text = "Só quero entrar na pista e disputar com alguém. Esse é o objetivo de tudo.", attack = 2, feel = 0, stage = 0, heat = 1, archetype = "aggressor", academy = "red_bull_junior" },
                    new OptionWeights { text = "Imagino tudo — a torcida, as luzes, meu nome aparecendo no telão.", attack = 0, feel = 0, stage = 2, heat = 0, archetype = "media_darling", academy = "" },
                    new OptionWeights { text = "Não muita coisa, na verdade. A temporada é longa. Este é apenas o primeiro dia.", attack = 0, feel = 0, stage = 0, heat = -2, archetype = "late_bloomer", academy = "renault_academy" }
                }
            },
            new Question
            {
                prompt = "Última volta, você está em segundo, o líder está logo à sua frente e falta apenas uma curva.",
                options = new[]
                {
                    new OptionWeights { text = "Vou partir para cima. Se houver uma brecha, ela é minha.", attack = 2, feel = 0, stage = 0, heat = 1, archetype = "aggressor", academy = "red_bull_junior" },
                    new OptionWeights { text = "Aceito o segundo lugar. Pontos agora, vitórias depois.", attack = -2, feel = 0, stage = 0, heat = 0, archetype = "technician", academy = "mercedes_junior" },
                    new OptionWeights { text = "Induzo o adversário ao erro e passo por ele sem dar chance para reação.", attack = 1, feel = 2, stage = 0, heat = 0, archetype = "prodigy", academy = "ferrari_academy" },
                    new OptionWeights { text = "Se a ultrapassagem nunca foi realmente possível, não vou acabar na brita na última volta.", attack = -1, feel = -2, stage = 0, heat = 0, archetype = "technician", academy = "" }
                }
            },
            new Question
            {
                prompt = "Seu engenheiro diz que o carro está ajustado para chuva, mas o fim de semana inteiro será seco.",
                options = new[]
                {
                    new OptionWeights { text = "Tudo bem. Vou tirar o máximo possível do carro, seja qual for a situação.", attack = 0, feel = 1, stage = 0, heat = 0, archetype = "late_bloomer", academy = "renault_academy" },
                    new OptionWeights { text = "Vou levá-lo a uma posição muito melhor no grid do que ele merece estar.", attack = 1, feel = 0, stage = 0, heat = 1, archetype = "prodigy", academy = "red_bull_junior" },
                    new OptionWeights { text = "Vou ficar cobrando o engenheiro o fim de semana inteiro até resolvermos isso.", attack = 0, feel = -1, stage = 0, heat = 0, archetype = "technician", academy = "mercedes_junior" },
                    new OptionWeights { text = "Levo o carro até o fim, garanto os pontos e não arrisco além do necessário.", attack = -2, feel = 0, stage = 0, heat = 0, archetype = "technician", academy = "" }
                }
            },
            new Question
            {
                prompt = "Um grande patrocinador quer você para uma sessão de fotos — um dia antes da classificação.",
                options = new[]
                {
                    new OptionWeights { text = "Ótimo. Vamos ser sinceros: os patrocinadores representam metade do trabalho aqui.", attack = 0, feel = 0, stage = 2, heat = 0, archetype = "media_darling", academy = "" },
                    new OptionWeights { text = "Vou fazer a sessão e depois me afastar de tudo para me concentrar no carro.", attack = 0, feel = -1, stage = -1, heat = 0, archetype = "prodigy", academy = "" },
                    new OptionWeights { text = "Prefiro não. Nada vem antes de pilotar.", attack = 0, feel = 0, stage = -2, heat = 0, archetype = "technician", academy = "mercedes_junior" },
                    new OptionWeights { text = "Vou fazer a sessão — e dizer algo que ainda estará dando o que falar na semana que vem.", attack = 0, feel = 0, stage = 2, heat = 1, archetype = "aggressor", academy = "" }
                }
            },
            new Question
            {
                prompt = "Alguém fecha sua passagem e acaba com a sua volta. O que você diz pelo rádio?",
                options = new[]
                {
                    new OptionWeights { text = "Vai ouvir poucas e boas. Não vou deixar isso passar em branco.", attack = 1, feel = 0, stage = 0, heat = 2, archetype = "aggressor", academy = "red_bull_junior" },
                    new OptionWeights { text = "Nada. Vou resolver isso na pista.", attack = 0, feel = 0, stage = 0, heat = -2, archetype = "prodigy", academy = "" },
                    new OptionWeights { text = "Apenas relato o ocorrido e sigo em frente. Não é meu trabalho fiscalizar os outros.", attack = 0, feel = 0, stage = 0, heat = -1, archetype = "technician", academy = "mercedes_junior" },
                    new OptionWeights { text = "Ah, acontece. Isso é corrida.", attack = 0, feel = 0, stage = 0, heat = -1, archetype = "late_bloomer", academy = "" }
                }
            },
            new Question
            {
                prompt = "Uma equipe maior te procura no meio da temporada. Aceitar significa deixar para trás quem te deu a primeira oportunidade.",
                options = new[]
                {
                    new OptionWeights { text = "Eu aceito. Este esporte não espera por sentimentalismo.", attack = 1, feel = 0, stage = 0, heat = 0, archetype = "prodigy", academy = "red_bull_junior" },
                    new OptionWeights { text = "Eu fico. Você não esquece quem te apoiou primeiro.", attack = 0, feel = 0, stage = 0, heat = 0, archetype = "technician", academy = "renault_academy" },
                    new OptionWeights { text = "Uso a oferta para conseguir um contrato melhor onde já estou.", attack = 0, feel = 0, stage = 1, heat = 0, archetype = "media_darling", academy = "" },
                    new OptionWeights { text = "Eu sigo as vitórias. Onde elas estiverem, é para lá que eu vou.", attack = 0, feel = 0, stage = 0, heat = 0, archetype = "prodigy", academy = "ferrari_academy" }
                }
            },
            new Question
            {
                prompt = "Você está alguns décimos atrás do seu companheiro de equipe. Qual é seu primeiro instinto?",
                options = new[]
                {
                    new OptionWeights { text = "Abrir a telemetria e encontrar até o último milissegundo de diferença.", attack = 0, feel = -2, stage = 0, heat = 0, archetype = "technician", academy = "ferrari_academy" },
                    new OptionWeights { text = "Mudar meu estilo de pilotagem até a volta encaixar.", attack = 0, feel = 2, stage = 0, heat = 0, archetype = "prodigy", academy = "" },
                    new OptionWeights { text = "Conversar com os engenheiros e mecânicos mais experientes.", attack = 0, feel = -1, stage = 0, heat = 0, archetype = "late_bloomer", academy = "renault_academy" },
                    new OptionWeights { text = "Forçar ainda mais. Vou tirar a diferença na raça e no esforço puro.", attack = 2, feel = 0, stage = 0, heat = 1, archetype = "aggressor", academy = "red_bull_junior" }
                }
            },
            new Question
            {
                prompt = "Um circuito novo que você nunca pilotou. Como você faz para dominá-lo?",
                options = new[]
                {
                    new OptionWeights { text = "Treinar à exaustão no simulador antes mesmo de pisar na pista.", attack = 0, feel = -2, stage = 0, heat = 0, archetype = "technician", academy = "mercedes_junior" },
                    new OptionWeights { text = "Umas poucas voltas na pista e já pego o traçado.", attack = 0, feel = 2, stage = 0, heat = 0, archetype = "prodigy", academy = "ferrari_academy" },
                    new OptionWeights { text = "Fazer o reconhecimento a pé, falar com os engenheiros e construir o ritmo curva a curva.", attack = 0, feel = -1, stage = 0, heat = 0, archetype = "technician", academy = "renault_academy" },
                    new OptionWeights { text = "Acelerar fundo logo de cara e descobrir o limite em alta velocidade.", attack = 2, feel = 0, stage = 0, heat = 1, archetype = "aggressor", academy = "red_bull_junior" }
                }
            },
            new Question
            {
                prompt = "A equipe pede para você manter posição atrás do companheiro pelo bem da temporada.",
                options = new[]
                {
                    new OptionWeights { text = "Tudo bem. Somos uma equipe — vou cumprir meu papel.", attack = 0, feel = 0, stage = 0, heat = -1, archetype = "technician", academy = "mercedes_junior" },
                    new OptionWeights { text = "Eu cumpro, mas não vou fingir que estou satisfeito.", attack = 0, feel = 0, stage = 0, heat = 1, archetype = "aggressor", academy = "" },
                    new OptionWeights { text = "Sem chance. Estou aqui para competir, não para seguir ninguém.", attack = 2, feel = 0, stage = 0, heat = 0, archetype = "prodigy", academy = "red_bull_junior" },
                    new OptionWeights { text = "Eu cumpro agora — mas vou cobrar quando for a minha vez.", attack = 0, feel = 1, stage = 0, heat = 0, archetype = "prodigy", academy = "" }
                }
            },
            new Question
            {
                prompt = "O que realmente faz você acordar cedo para correr?",
                options = new[]
                {
                    new OptionWeights { text = "A sensação. Nada no mundo supera a sensação de uma volta perfeita e rápida.", attack = 0, feel = 0, stage = 0, heat = 1, archetype = "aggressor", academy = "" },
                    new OptionWeights { text = "Vencer. É só isso. Esse é todo o propósito.", attack = 1, feel = 0, stage = 0, heat = 0, archetype = "prodigy", academy = "red_bull_junior" },
                    new OptionWeights { text = "Provar que todos que duvidaram de mim estavam errados.", attack = 1, feel = 0, stage = 0, heat = 1, archetype = "aggressor", academy = "independent" },
                    new OptionWeights { text = "Construir algo duradouro — ser um piloto completo no futuro.", attack = 0, feel = -1, stage = 0, heat = 0, archetype = "technician", academy = "mercedes_junior" }
                }
            },
            new Question
            {
                prompt = "Quando tudo dá errado em uma corrida, como você reage?",
                options = new[]
                {
                    new OptionWeights { text = "Fico em silêncio, guardo para mim e volto com raiva na semana seguinte.", attack = 0, feel = 0, stage = 0, heat = -1, archetype = "prodigy", academy = "" },
                    new OptionWeights { text = "Isso me consome por dentro. Eu sempre mostro o que sinto abertamente.", attack = 0, feel = 0, stage = 0, heat = 2, archetype = "aggressor", academy = "red_bull_junior" },
                    new OptionWeights { text = "Eu dou de ombros. É apenas uma corrida em uma longa carreira.", attack = 0, feel = 0, stage = 0, heat = -2, archetype = "late_bloomer", academy = "renault_academy" },
                    new OptionWeights { text = "Assumo publicamente, dou meu recado e sigo em frente. Mantém meu nome nos holofotes.", attack = 0, feel = 0, stage = 2, heat = 0, archetype = "media_darling", academy = "" }
                }
            },
            new Question
            {
                prompt = "Decisão do título. As luzes estão prestes a se apagar. O que passa pela sua cabeça?",
                options = new[]
                {
                    new OptionWeights { text = "Absolutamente nada. Apenas o ponto de frenagem da primeira curva.", attack = 0, feel = 0, stage = 0, heat = -2, archetype = "prodigy", academy = "mercedes_junior" },
                    new OptionWeights { text = "Eu quero esse título mais do que eles, e eu sei disso.", attack = 1, feel = 0, stage = 0, heat = 2, archetype = "aggressor", academy = "red_bull_junior" },
                    new OptionWeights { text = "O plano tático. Eu já repassei todos os cenários possíveis dessa corrida na mente.", attack = 0, feel = -2, stage = 0, heat = 0, archetype = "technician", academy = "ferrari_academy" },
                    new OptionWeights { text = "O barulho, o público, a energia. Meu Deus, como eu amo isso.", attack = 0, feel = 0, stage = 2, heat = 0, archetype = "media_darling", academy = "" }
                }
            },
            new Question
            {
                prompt = "Daqui a dez anos — o que significará que você \"chegou ao topo\"?",
                options = new[]
                {
                    new OptionWeights { text = "Ser Campeão do Mundo. Simples assim.", attack = 1, feel = 0, stage = 0, heat = 0, archetype = "prodigy", academy = "ferrari_academy" },
                    new OptionWeights { text = "Ainda estar correndo, aproveitando cada momento e estando aqui.", attack = 0, feel = 0, stage = 0, heat = -1, archetype = "late_bloomer", academy = "renault_academy" },
                    new OptionWeights { text = "Um nome respeitado e conhecido por todos — dentro e fora das pistas.", attack = 0, feel = 0, stage = 2, heat = 0, archetype = "media_darling", academy = "" },
                    new OptionWeights { text = "O piloto que todo engenheiro sonha em ter no carro.", attack = 0, feel = -1, stage = -1, heat = 0, archetype = "technician", academy = "mercedes_junior" }
                }
            },
            new Question
            {
                prompt = "No fundo, quem você quer ser quando estiver na pista?",
                options = new[]
                {
                    new OptionWeights { text = "O garoto que fez coisas que ninguém achava serem possíveis.", attack = 0, feel = 1, stage = 0, heat = 0, archetype = "prodigy", academy = "ferrari_academy" },
                    new OptionWeights { text = "Aquele que faz as pessoas comprarem ingresso só para ver guiar.", attack = 2, feel = 0, stage = 1, heat = 1, archetype = "aggressor", academy = "red_bull_junior" },
                    new OptionWeights { text = "Aquele que é cirúrgico e nunca, jamais erra uma curva.", attack = -1, feel = -2, stage = 0, heat = 0, archetype = "technician", academy = "mercedes_junior" },
                    new OptionWeights { text = "Aquele que veio do nada e superou todos os adversários na resistência.", attack = 0, feel = 0, stage = 0, heat = -1, archetype = "late_bloomer", academy = "independent" }
                }
            },
        };
    }
}
