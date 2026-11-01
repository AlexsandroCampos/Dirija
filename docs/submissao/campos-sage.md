# Campos do formulário SAGe (texto pronto para copiar)

Conferir no SAGe o limite de caracteres de cada campo antes de colar. Os resumos abaixo têm cerca de 1.400 caracteres.

## Título
**Português:** Busca geoespacial e reserva concorrente de aulas práticas de direção com instrutores autônomos: prova de conceito da plataforma Dirija!
**Inglês:** Geospatial search and concurrent booking of practical driving lessons with independent instructors: proof of concept of the Dirija! platform

## Resumo (português)
A Resolução CONTRAN nº 1.020/2025 permitiu que as aulas práticas para a primeira habilitação sejam dadas por instrutores autônomos credenciados. O aplicativo oficial CNH do Brasil já permite localizar esses instrutores, mas a marcação do horário e o pagamento são combinados diretamente entre aluno e instrutor. Este projeto estuda dois problemas técnicos dessa intermediação: a busca por proximidade com baixa latência e a reserva de horários sem duplicidade quando vários alunos tentam reservar o mesmo horário ao mesmo tempo. A equipe já construiu um MVP em ASP.NET Core 8 com SQLite, com busca em mapa, agenda, bloqueio do horário por 10 minutos, pagamento simulado e 63 testes automatizados, incluindo um teste em que 50 reservas simultâneas resultam em apenas uma aceita. Na Fase 1, o sistema será levado para PostgreSQL/PostGIS; serão comparadas estratégias de indexação espacial e de controle de concorrência sob carga de até 500 usuários virtuais; será criado um painel mínimo para o instrutor; e a usabilidade será avaliada com alunos e instrutores. As metas são latência p95 da busca abaixo de 300 ms com 10 mil instrutores, zero reservas duplicadas, ao menos 80% de sucesso nas tarefas e nota SUS de pelo menos 70.

## Resumo (inglês)
Brazil's CONTRAN Resolution 1,020/2025 allowed practical lessons for a first driver's license to be taught by accredited independent instructors. The official CNH do Brasil app already lets learners find these instructors, but scheduling and payment are still arranged directly between learner and instructor. This project studies two technical problems of this intermediation: low-latency proximity search and booking time slots without duplicates when several learners try to book the same slot at the same time. The team has already built an MVP in ASP.NET Core 8 with SQLite, featuring map search, instructor schedules, a 10-minute slot hold, simulated payment and 63 automated tests, including one in which 50 simultaneous bookings result in a single accepted reservation. In Phase 1, the system will be moved to PostgreSQL/PostGIS; spatial indexing and concurrency control strategies will be compared under loads of up to 500 virtual users; a minimal instructor dashboard will be built; and usability will be evaluated with learners and instructors. The targets are p95 search latency below 300 ms with 10,000 instructors, zero duplicate bookings, at least 80% task success and a SUS score of at least 70.

## Palavras-chave
**Português:** busca geoespacial; controle de concorrência; sistemas de reserva; plataformas digitais; formação de condutores.
**Inglês:** geospatial search; concurrency control; booking systems; digital platforms; driver education.

## Área do conhecimento (tabela CNPq usada pelo SAGe — conferir no campo)
- Grande área: Ciências Exatas e da Terra
- Área: Ciência da Computação (1.03.00.00-7)
- Subárea: Metodologia e Técnicas da Computação (1.03.03.00-6)
- Especialidade: Sistemas de Informação (1.03.03.04-9); secundária: Banco de Dados (1.03.03.03-0)

## Duração e datas
- Duração: 9 meses (Fase 1). Data de início: [definida pela FAPESP na concessão].

## Orçamento por alínea (aba "R$/US$")

| Alínea | Itens | Valor (R$) |
|---|---|---:|
| Material permanente (nacional) | 3 smartphones de teste | 7.200,00 |
| Material de consumo (nacional) | 3 linhas pré-pagas × 9 meses | 1.350,00 |
| Serviços de terceiros – PJ (nacional) | Nuvem (6.300), testes de carga (2.000), avaliação heurística (5.000) | 13.300,00 |
| Serviços de terceiros – consultoria | Consultoria jurídica | 10.000,00 |
| Bolsas | Não solicitadas | 0,00 |
| **Total** | | **31.850,00** |

Reserva Técnica: calculada pela FAPESP, não informar. Se o serviço de testes de carga for cobrado em dólar por empresa estrangeira, lançar S2 como serviço de terceiros importado (US$ 300 + aumento temporário do banco em reais).

## Pessoas envolvidas (aba "Dados Gerais do Projeto" > "Pessoas Envolvidas")
Cada pessoa precisa ter cadastro completo no SAGe e confirmar a participação.

| Nome | Função no SAGe | Horas/semana |
|---|---|---:|
| Alexsandro de Souza Campos | Pesquisador Responsável | 24 |
| Matheus Silva Portes de Lima | Colaborador | 10 |
| Matheus Machado Alfradique | Colaborador | 10 |
| Ana Luisa Oliveira da Silva | Colaboradora | 10 |
| Gabriel Marchesano | Colaborador | 10 |
| [Consultor jurídico] | Consultor | 40 h no total |

## Empresa
Submeter como "Empresa a constituir" (9 f). A submissão é feita por Alexsandro, que será sócio da empresa.
