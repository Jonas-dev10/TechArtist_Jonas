# Tech Artist Test — Jonas Mello

## Como executar o projeto

1. Abra o projeto no Unity versão **6000.0.62f1**
2. Abra a cena `Assets/Scenes/TechArtistShowcase`
3. A cena está dividida em três seções demonstrativas:
   - **SHADER TECNICO** — objetos com highlight, Fresnel e Bloom
   - **XRAY DEMO** — demonstração do efeito X-Ray através de objetos
   - **MESH COMBINER DEMO** — objetos prontos para demonstrar a ferramenta

---

## 1. Shader Técnico — HighlightClipping

### O que foi implementado

- Highlight de seleção com cor controlável via Inspector
- Controle de intensidade via slider
- Efeito Fresnel com cor independente — brilho nas bordas do objeto
- Clipping Plane ajustável via Inspector (Range -5 a 5)
- Modo X-Ray — objeto visível através de outros objetos
- Bloom via Post Processing Volume — realça o brilho do Fresnel
- Compatível com URP
- Depth Fade — dissolve suavemente na intersecção com outras superfícies

### Decisões técnicas

**HLSL puro em vez de Shader Graph**
O shader foi escrito inteiramente em HLSL para maior controle sobre o 
pipeline de renderização e melhor compatibilidade com o SRP Batcher do URP.

**X-Ray via segundo material**
O URP não suporta múltiplos passes por shader nativamente. A solução 
adotada foi separar o efeito X-Ray em um shader independente 
(`XRayHighlight`) aplicado como segundo material no Mesh Renderer. 
Em produção, a solução ideal seria um Renderer Feature customizado no URP.

**Sincronização do Clip Plane**
Os dois materiais (Highlight e X-Ray) compartilham o parâmetro 
`_ClipPlaneHeight`. Em produção, um script C# centralizaria o controle — 
um único slider atualizaria ambos os materiais simultaneamente, 
abstraindo a complexidade técnica para o artista.

**Fresnel com cor independente**
A cor do Fresnel foi separada da cor base do highlight, permitindo 
controle total — por exemplo, highlight amarelo com borda branca ou ciano.

**Pulsação animada — decisão consciente de não implementar**
A pulsação foi avaliada mas não implementada. Um highlight técnico de 
seleção deve ser estável e previsível para o artista. O efeito seria 
mais adequado para highlights de gameplay como itens coletáveis ou alvos 
de missão — não para uma ferramenta técnica de pipeline.

**Depth Fade**
O depth fade compara a profundidade do pixel atual com a profundidade 
da cena via `_CameraDepthTexture` do URP. Quando o objeto se aproxima 
de outra superfície, o alpha dissolve suavemente evitando bordas duras 
de intersecção. O efeito é independente do clipping plane — que mantém 
corte preciso por design.

---

## 2. Ferramenta de Mesh Combine

### Como usar

1. Selecione dois ou mais objetos na cena com **Ctrl+Click**
2. Acesse **Tools → Mesh Combiner → Combine Selected Objects**
3. Ou use o atalho **Ctrl+Shift+M**

### O que a ferramenta faz

- Agrupa automaticamente os objetos selecionados por material
- Combina apenas meshes que compartilham o mesmo material
- Cria um objeto combinado nomeado com o material e os objetos originais
- Organiza os objetos originais em um Empty identificado e o desativa
- Objetos originais permanecem ativos dentro do grupo — fácil recuperação
- Suporte a meshes grandes com mais de 65k vértices (IndexFormat.UInt32)
- Bounds recalculado automaticamente — culling e clipping corretos
- Gera relatório de redução de draw calls no Console
- Suporta desfazer com **Ctrl+Z**
- Atalho rápido **Ctrl+Shift+M**
- EditorWindow dedicada com opções configuráveis via Tools → Mesh Combiner
- Toggle "Combine by Material" — agrupa por material ou combina tudo
- Toggle "Keep Originals" — mantém originais organizados ou remove após combinação
- Botão desabilitado automaticamente com menos de 2 objetos selecionados

### Decisões técnicas

**Agrupamento por material**
A combinação respeita os materiais — objetos com materiais diferentes 
são combinados em grupos separados, mantendo a renderização correta.

**Organização dos originais**
Os objetos originais não são deletados — são movidos para um Empty 
identificado (`MeshCombiner_Originals_[Material]`) e desativados. 
Os objetos dentro do grupo permanecem ativos, permitindo recuperação 
individual fácil e desfazer completo via Undo.

**localToWorldMatrix**
Cada mesh é transformado para o espaço do mundo antes de combinar, 
garantindo que as posições absolutas dos objetos sejam preservadas 
corretamente no mesh resultante.

**IndexFormat.UInt32**
Por padrão o Unity limita meshes a 65.535 vértices. A ferramenta usa 
UInt32 para suportar meshes complexos sem risco de erro em cenas grandes.

**RecalculateBounds**
O bounds é recalculado após a combinação para garantir que o Frustum 
Culling e o Clipping funcionem corretamente no mesh resultante.

**EditorWindow dedicada**
A ferramenta evoluiu de um simples MenuItem para uma EditorWindow 
completa. A janela oferece contador de objetos selecionados em tempo 
real, opções configuráveis de combinação e botão desabilitado 
automaticamente quando menos de 2 objetos estão selecionados — 
prevenindo erros de uso. O atalho Ctrl+Shift+M foi mantido para 
fluxo rápido sem abrir a janela.

---

## Diferenciais avaliados e não implementados

**Stencil Buffer**
O uso de stencil buffer foi avaliado como alternativa ao ZTest Greater 
do X-Ray — especialmente para compatibilidade com WebGL 1.0. A abordagem 
seria usar dois passes: o primeiro escreve no stencil buffer, o segundo 
renderiza apenas onde o stencil está marcado, evitando dependência do 
depth test. Em produção seria a abordagem preferida por ter suporte mais 
amplo entre plataformas.

**Preservar Metadata**
A preservação de metadata foi avaliada mas não implementada dentro do prazo. 
A abordagem seria usar `AssetDatabase.CreateAsset()` para salvar o mesh 
combinado como `.asset` reutilizável em disco, preservando nome, layer, 
tag e referências dos objetos originais em um ScriptableObject auxiliar.

**EditorWindow — melhorias futuras identificadas**
A EditorWindow implementada atende ao escopo do teste. Melhorias 
identificadas para uma versão de produção:
- Preview de redução de draw calls antes de confirmar a operação
- Agrupamento por tag ou layer além de material
- Opção de salvar mesh combinado como asset reutilizável via 
  `AssetDatabase.CreateAsset()`
- Geração automática de colliders no objeto combinado
- Barra de progresso para cenas com muitos objetos via 
  `EditorUtility.DisplayProgressBar()`

---

## Respostas às perguntas técnicas

### 1. Como adaptaria o shader para melhorar performance em WebGL, Meta Quest e dispositivos Mobile?

**Mobile e Meta Quest:**

- Mover o cálculo do Fresnel do fragment shader para o vertex shader — 
reduz o custo de por pixel para por vértice, crítico em GPUs mobile 
com milhões de pixels por frame
- Substituir transparência (`Blend SrcAlpha OneMinusSrcAlpha`) por 
Alpha Cutout (`clip(alpha - 0.5)`) para eliminar overdraw — 
especialmente importante no Meta Quest que renderiza dois olhos 
simultaneamente
- Usar precisão `half` em vez de `float` nas variáveis do fragment 
shader — GPUs mobile operam melhor com 16 bits
- Limitar o número de objetos com highlight ativo simultaneamente

**Meta Quest especificamente:**

- Adicionar suporte a Single Pass Instanced Rendering com 
`#pragma multi_compile _ UNITY_SINGLE_PASS_STEREO` — renderiza os 
dois olhos em um único pass, reduzindo o custo à metade

**WebGL especificamente:**

- O `ZTest Greater` usado no X-Ray pode ter comportamento inconsistente 
em WebGL 1.0 dependendo do navegador e GPU — garantir WebGL 2.0 no 
Player Settings ou substituir por abordagem com Stencil Buffer
- Minimizar o número de passes de renderização

---

### 2. Em quais situações não é recomendado combinar meshes?

- **Objetos com interação do jogador** — destruição, coleta, highlight 
individual — não é possível selecionar ou afetar partes de um mesh 
combinado separadamente
- **Objetos com física** — Rigidbody e colisão individual se tornam 
inviáveis após a combinação, pois o mesh se comporta como um bloco único
- **Objetos com LOD** — o sistema de LOD do Unity opera por objeto; 
meshes combinados perdem a granularidade de qualidade por distância
- **Objetos que se movem** — animação, rotação ou translação individual 
é impossível após a combinação
- **Objetos muito distantes entre si** — o Frustum Culling trata o mesh 
combinado como um objeto único; se qualquer parte estiver visível, 
o mesh inteiro é renderizado mesmo que 90% esteja fora da tela

**Recomendação geral:** Mesh Combine é ideal para geometria estática de 
cenário sem interação — props de decoração, estruturas de fundo e 
composição de ambiente.

---

### 3. Quais estratégias podem ser utilizadas para reduzir shader variants no Unity?

- **Separar em shaders menores com responsabilidade única** — aplicado 
neste projeto: o X-Ray foi separado em shader independente em vez de 
usar toggle, eliminando variants desnecessárias
- **Usar `shader_feature` em vez de `multi_compile`** — `shader_feature` 
compila apenas as variants realmente utilizadas no projeto; 
`multi_compile` compila todas as combinações possíveis independente do uso
- **Shader Variant Stripping** — configurar o stripping automático nas 
Project Settings para remover variants não utilizadas durante o build
- **Shader Variant Collection** — pré-aquecer apenas as variants 
necessárias para evitar hitching durante o jogo quando um shader é 
compilado pela primeira vez em runtime
- **Minimizar keywords** — cada keyword adicional dobra o número de 
variants; avaliar se uma feature realmente precisa de keyword ou pode 
ser controlada por propriedade

---

## Estrutura do projeto

```
Assets/
├── Materials/          — materiais utilizados na cena
├── Meshes/             — meshes gerados pela ferramenta
├── Scenes/             — cena principal TechArtistShowcase
├── Scripts/
│   └── Editor/         — ferramentas do Editor (MeshCombinerTool.cs)
└── Shaders/            — shaders HLSL (HighlightClipping, XRayHighlight)
```