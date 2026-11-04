import { ApiError, getJson } from './apiClient';
import { CurrentUser, isCurrentUser } from '../types/currentUser';

export async function getCurrentUser(signal?: AbortSignal): Promise<CurrentUser> {
    const json = await getJson('/me', signal);
    if (!isCurrentUser(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de ingelogde gebruiker.');
    return json;
}
