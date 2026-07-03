========================================================
 SIM FFB - Force Feedback sintetico para FiveM (G923)
========================================================

QUE ES
  Da sensacion de Force Feedback al volante en FiveM (que por si mismo
  NO soporta FFB). Un resource de FiveM saca la telemetria del coche y
  este programa la convierte en fuerzas en el volante.
  No se inyecta en FiveM (solo escucha un WebSocket local) -> anti-cheat safe.

PIEZAS
  1) Resource FiveM  ->  sim_ffb   (carpeta aparte, va en tu servidor)
  2) Este programa   ->  SimFfb.exe (se ejecuta en tu PC)

INSTALACION
  1. Copia la carpeta 'sim_ffb' a los resources de tu servidor FiveM
     y anade en server.cfg:   ensure sim_ffb
  2. (Opcional) tambien 'sim_drive' para marchas/HUD/direccion de volante.

USO
  1. Enciende el G923 y su software (G HUB) + tu x360ce (mando Xbox virtual).
  2. Ejecuta  SimFfb.exe
  3. Entra a FiveM, sube a un coche -> deberia decir "FiveM conectado"
     y el volante cobra vida (autocentrado, golpes, vibracion...).

AJUSTES
  Edita  ffb.cfg  y reinicia SimFfb.exe (no hace falta recompilar).
  - CenterGain: fuerza de autocentrado
  - RumbleGain: vibracion   - ImpactGain: golpes
  - DriftLighten: aligerar al derrapar
  - Invert=1 si el volante tira al reves

NOTAS HONESTAS
  - Es FFB SINTETICO (calculado con telemetria aproximada). No es Assetto,
    pero da sensacion util: centrado, golpes, curbs, perdida de agarre.
  - Si al ejecutar SimFfb.exe el volante deja de girar en el juego, prueba
    el orden de arranque (primero G HUB, luego SimFfb) - el FFB usa
    acceso exclusivo del volante.
  - Force Feedback REAL nativo en GTA V solo existe con el mod .asi de ikt,
    que FiveM banea. Esta es la unica via segura.
