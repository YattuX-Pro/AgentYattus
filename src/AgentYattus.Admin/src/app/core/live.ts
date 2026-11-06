import { Injectable, inject, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { MissionStatus } from './api';
import { AuthService } from './auth';

export interface MissionEvent { missionId: string; status: MissionStatus; at: string }

/**
 * Le direct des missions (hub SignalR /hubs/missions, leçon 15). Le serveur n'envoie à chacun que les missions
 * qu'il peut lire (leçon 48). Le jeton part dans l'adresse : un navigateur ne peut pas mettre d'en-tête sur un WebSocket.
 */
@Injectable({ providedIn: 'root' })
export class LiveService {
  private readonly auth = inject(AuthService);
  private connection?: HubConnection;

  readonly events = new Subject<MissionEvent>();
  readonly connected = signal(false);

  start() {
    if (this.connection) return;
    this.connection = new HubConnectionBuilder()
      .withUrl('/api/hubs/missions', { accessTokenFactory: () => this.auth.accessToken ?? '' })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    this.connection.on('MissionEvent', (event: MissionEvent) => this.events.next(event));
    this.connection.onreconnecting(() => this.connected.set(false));
    this.connection.onreconnected(() => this.connected.set(true));
    this.connection.onclose(() => this.connected.set(false));
    void this.connect();
  }

  private async connect() {
    try {
      await this.connection!.start();
      this.connected.set(this.connection!.state === HubConnectionState.Connected);
    } catch {
      // API arrêtée : on réessaie dans 5 s ; la reconnexion automatique ne couvre que les coupures après une première connexion.
      setTimeout(() => void this.connect(), 5000);
    }
  }
}
