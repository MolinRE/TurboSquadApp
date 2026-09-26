import type { SwipesApi } from "./contract";
import { fakeSwipesApi } from "./fake-api";

/** Точка, через которую экран ходит за Сменой. Пока API Смены нет, это подменный модуль. */
export const swipesApi: SwipesApi = fakeSwipesApi;
